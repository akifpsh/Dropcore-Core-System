using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMod", menuName = "Mod Data")]
public class ModeData : ScriptableObject
{
    public string modeName; 
    public string description; 
    public Sprite icon;
    public List<DynamicSettings> dynamicSettings; 
    public List<StaticSettings> staticSettings;

    private void OnEnable()
    {
        if (dynamicSettings == null) return;

        foreach (var setting in dynamicSettings)
        {
            setting.currentValue = setting.defaultValue;
            setting.isOn = true;
        }
    }
}

public enum SettingType
{
    Stepper,
    Switch   
}

[System.Serializable]
public class DynamicSettings
{
    public string settingName;
    public SettingType type;

    public int minValue; 
    public int maxValue; 
    public int stepValue; 
    public int defaultValue; 
    public int currentValue; 
    public bool isOn; 
}

[System.Serializable]
public class StaticSettings
{
    public string key;
    public float value; 
}

