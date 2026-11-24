using System;
using UnityEngine;

namespace HMI.Protocol
{
    // ========== 基类 ==========
    [Serializable]
    public class WebSocketMessage
    {
        public string type;
        public string payload; // JSON字符串，使用时二次解析
    }

    // ========== Android → Unity 指令 ==========

    [Serializable]
    public class SceneSwitchPayload
    {
        public string scene; // "drive" | "park"
    }

    [Serializable]
    public class DataSourceSwitchPayload
    {
        public string mode; // "mock" | "hardware"
    }

    [Serializable]
    public class ParkingControlPayload
    {
        public string action; // "start" | "pause" | "exit"
    }

    [Serializable]
    public class VoiceCommandPayload
    {
        public string command; // "open_drive" | "open_park"
    }

    // ========== Unity → Android 状态 ==========

    [Serializable]
    public class SceneCurrentPayload
    {
        public string scene; // "drive" | "park"
    }

    [Serializable]
    public class VehicleStatePayload
    {
        public int speed;
        public string gear;
        public string mode;
        public int frontDistance;
        public bool hasPedestrian;
        public bool hasObstacle;
        public bool laneDeparture;
        public int warningLevel; // 0=安全 1=注意 2=危险
    }

    [Serializable]
    public class ParkingStatePayload
    {
        public double front;
        public double rear;
        public double left;
        public double right;
        public string status; // "泊车中" | "已就绪" | "已完成"
    }

    [Serializable]
    public class RiskWarningPayload
    {
        public int level; // 0=安全 1=注意 2=危险
        public string message;
    }

    // ========== 新增：快照消息结构 ==========

    [System.Serializable]
    public class SnapshotPayload
    {
        public long timestamp;
        public ProcessedData signals;
        public ProcessedData processed;
    }

    [System.Serializable]
    public class ProcessedData
    {
        public int speed;
        public string gear;
        public int power;
        public int battery;
        public int oil;
        public int adas_status;
        public int warning_level;
        public string warning_text;
        public bool has_warning;
        public bool door_open;
        public float tire_pressure_fl;
        public float tire_pressure_fr;
        public float tire_pressure_rl;
        public float tire_pressure_rr;
    }

    [System.Serializable]
    public class SignalPayload
    {
        public string name;
        public string value;
        public string valueType;
        public bool isState;
    }
    /// <summary>
    /// 路面信息
    /// </summary>
    [System.Serializable]
    public class RoadClass
    {
        public double xPos;
        public double yPos;
        public double zPos;
        public double perWidth;
        public double rot;//正为向右，负为向左
        public double laneCount;
    }

}