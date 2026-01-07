using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PhotonView), typeof(Rigidbody2D))]
public class PlayerNetworkSync : MonoBehaviourPun, IPunObservable
{
    [Header("Base Tuning")]
    [Tooltip("Minimum render buffer (seconds).")]
    [SerializeField] private float minBuffer = 0.08f; 

    [Tooltip("Maximum render buffer (seconds).")]
    [SerializeField] private float maxBuffer = 0.15f;

    [Tooltip("Hard cap for extrapolation. Shotgun gibi hızlı silahlar için biraz artırdık.")]
    [SerializeField] private float maxExtrapolation = 0.25f; 

    [Tooltip("Max stored snapshots.")]
    [SerializeField] private int maxBufferSize = 24;

    [Header("Buffer Smoothing")]
    // KRİTİK AYAR: Ani lag veya Shotgun vuruşunda tamponun anında tepki vermesi için yüksek olmalı.
    [SerializeField] private float bufferRiseRate = 3.0f; // Shotgun Fix'i (Eskisi düşüktü)

    [Tooltip("Tamponun düşme hızı. Çok düşük olmalı ki jitter yapmasın.")]
    [SerializeField] private float bufferFallRate = 0.01f; // Jitter Fix'i (Eskisi 0.08 çok hızlıydı)

    [Header("Extra Smoothing (optional)")]
    [SerializeField] private float positionSmoothing = 0f;

    [Header("Debug")]
    [SerializeField] private bool logNetStats = false;
    [SerializeField] private float logInterval = 1.0f;

    [Header("Teleport Logic")]
    [Tooltip("Eğer mesafe bu değerden büyükse (metre), interpolate etme direkt ışınla.")]
    [SerializeField] private float teleportThreshold = 5.0f;

    private Rigidbody2D rb;
    private PlayerController controller;

    private struct State
    {
        public double t;
        public Vector2 pos;
        public Vector2 vel;
    }

    private readonly List<State> states = new List<State>(32);
    private double lastRecvTs = -1;

    private Vector2 desiredPos;
    private Vector2 smoothedPos;
    private bool hasDesired;

    private float currentBufferVal;

    // Arrival spacing stats (EWMA)
    private bool hasDt;
    private float dtMean = 0.033f;
    private float dtVar = 0.0000f;
    private const float DtAlpha = 0.10f;

    // Debug stats
    private float logTimer;
    private int interpCount, extrapCount, holdCount, oooCount;
    private float extrapDtMax, extrapDtSum;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        controller = GetComponent<PlayerController>();

        if (!photonView.IsMine)
        {
            // Remote: Kinematic, fizik motoru karışmasın
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            // Remote karakteri Update'te süreceğimiz için Interpolate'e gerek yok
            rb.interpolation = RigidbodyInterpolation2D.None; 
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        }
        else
        {
            // Local: Normal fizik
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        desiredPos = rb.position;
        smoothedPos = desiredPos;
        hasDesired = false;

        currentBufferVal = minBuffer; 
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (rb == null) return;

        if (stream.IsWriting)
        {
            stream.SendNext(rb.position);
            stream.SendNext(rb.velocity);
            stream.SendNext(controller != null ? controller.isGrounded : false);
        }
        else
        {
            Vector2 pos = (Vector2)stream.ReceiveNext();
            Vector2 vel = (Vector2)stream.ReceiveNext();
            stream.ReceiveNext(); 
            double ts = info.SentServerTime;

            if (lastRecvTs >= 0 && ts <= lastRecvTs)
            {
                oooCount++;
                return;
            }

            if (lastRecvTs >= 0)
            {
                float dt = (float)(ts - lastRecvTs);
                if (dt > 0.0005f && dt < 0.5f)
                {
                    if (!hasDt)
                    {
                        hasDt = true;
                        dtMean = dt;
                        dtVar = 0f;
                    }
                    else
                    {
                        float diff = dt - dtMean;
                        dtMean += DtAlpha * diff;
                        dtVar = Mathf.Max(0f, (1f - DtAlpha) * (dtVar + DtAlpha * diff * diff));
                    }
                }
            }

            lastRecvTs = ts;

            states.Add(new State { t = ts, pos = pos, vel = vel });
            if (states.Count > maxBufferSize)
                states.RemoveAt(0);
        }
    }

    private void Update()
    {
        if (photonView.IsMine || rb == null) return;
        
        CalculateRenderTimeAndPos();

        if (hasDesired)
        {
            Vector2 applyPos = desiredPos;

            if (positionSmoothing > 0f)
            {
                float k = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
                smoothedPos = Vector2.Lerp(smoothedPos, desiredPos, k);
                applyPos = smoothedPos;
            }
            else
            {
                smoothedPos = desiredPos;
            }

            rb.MovePosition(applyPos);
        }
    }

    private void CalculateRenderTimeAndPos()
    {
        if (states.Count == 0) return;

        float pingS = Mathf.Clamp(PhotonNetwork.GetPing() * 0.001f, 0f, 0.25f);
        float tick = Mathf.Clamp(dtMean, 0.016f, 0.10f);
        float tickStd = Mathf.Sqrt(dtVar);

        // FORMÜL: 3.5f tick ile daha "safe" bir buffer
        float targetBuffer = (3.5f * tick) + (2.0f * tickStd) + (0.25f * pingS);
        targetBuffer = Mathf.Clamp(targetBuffer, minBuffer, maxBuffer);

        // Buffer Smoothing
        float rate = (targetBuffer > currentBufferVal) ? bufferRiseRate : bufferFallRate;
        currentBufferVal = Mathf.MoveTowards(currentBufferVal, targetBuffer, rate * Time.unscaledDeltaTime);

        float dynMaxEx = Mathf.Clamp(currentBufferVal * 2.0f, 0.06f, maxExtrapolation);

        double renderTime = PhotonNetwork.Time - currentBufferVal;

        // Hold oldest logic
        if (renderTime <= states[0].t)
        {
            holdCount++;
            SetDesired(states[0].pos);
            TryLog(targetBuffer, currentBufferVal, dynMaxEx);
            return;
        }

        int n = states.Count;
        for (int i = n - 2; i >= 0; i--)
        {
            State a = states[i];
            State b = states[i + 1];

            if (a.t <= renderTime && renderTime <= b.t)
            {
                float span = (float)(b.t - a.t);
                float t = span > 1e-6f ? (float)((renderTime - a.t) / span) : 0f;
                t = Mathf.Clamp01(t);

                interpCount++;
                SetDesired(Vector2.Lerp(a.pos, b.pos, t));
                TryLog(targetBuffer, currentBufferVal, dynMaxEx);
                return;
            }
        }

        State last = states[n - 1];
        float extraDt = (float)(renderTime - last.t);
        extraDt = Mathf.Clamp(extraDt, 0f, dynMaxEx);

        extrapCount++;
        extrapDtSum += extraDt;
        if (extraDt > extrapDtMax) extrapDtMax = extraDt;

        SetDesired(last.pos + last.vel * extraDt);
        TryLog(targetBuffer, currentBufferVal, dynMaxEx);
    }

    private void SetDesired(Vector2 p)
    {
        float dist = Vector2.Distance(rb.position, p);

        // TELEPORT LOGIC
        if (dist > teleportThreshold)
        {
            rb.position = p;
            desiredPos = p;
            smoothedPos = p;
            states.Clear();
            lastRecvTs = -1;
            hasDesired = true;
            return;
        }

        desiredPos = p;
        if (!hasDesired)
        {
            smoothedPos = p;
            hasDesired = true;
        }
    }

    private void TryLog(float targetBuffer, float currentBuffer, float dynMaxEx)
    {
        if (!logNetStats || logInterval <= 0f) return;

        logTimer += Time.unscaledDeltaTime;
        if (logTimer < logInterval) return;
        logTimer = 0f;

        int total = interpCount + extrapCount + holdCount;
        float exPct = total > 0 ? (extrapCount * 100f / total) : 0f;
        float exAvg = extrapCount > 0 ? (extrapDtSum / extrapCount) : 0f;

        DebugManager.Log(
            $"[NETSYNC] Ping={PhotonNetwork.GetPing()}ms | " +
            $"tick(mean/std)={(dtMean * 1000f):F1}/{(Mathf.Sqrt(dtVar) * 1000f):F1}ms | " +
            $"BUF={states.Count}/{maxBufferSize} | " +
            $"Interp={interpCount} Hold={holdCount} Extrap={extrapCount} ({exPct:F1}%) | " +
            $"targetBuf={targetBuffer:F3}s curBuf={currentBuffer:F3}s"
        );

        interpCount = extrapCount = holdCount = 0;
        extrapDtSum = extrapDtMax = 0f;
        oooCount = 0;
    }
}