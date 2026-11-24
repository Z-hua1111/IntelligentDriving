using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using HMI.Protocol;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;       // ✅ 确保顶部有这行

namespace HMI.Communication
{
    public class WebSocketClient : MonoBehaviour
    {
        [Header("连接配置")]
        public string serverUrl = "ws://10.0.2.2:8080";
        public float reconnectInterval = 3f;

        [Header("联动引用")]
        public ViewSwitchController viewController;
        public ObstacleManager obstacleManager;
        public ObstacleVisualEnhancer visualEnhancer;
        public LaneLineManager laneLineManager;
        public CarWASDControl carController;
        public RoadRenderManager roadRenderManager;

        private ClientWebSocket websocket;
        private CancellationTokenSource cts;
        private bool isConnected = false;
        private bool isReconnecting = false;

        // 泊车状态上报：AutoParkingController 完成后原本没有任何订阅者，
        // 导致安卓端永远收不到 parking.state=completed，仪表速度卡在 5。
        private AutoParkingController parkingController;
        private bool parkingHooked = false;

        private Queue<string> messageQueue = new Queue<string>();
        private readonly object queueLock = new object();

        public bool IsConnected => isConnected;

        // ========== 消息实体类 ==========

        [System.Serializable]
        public class WebSocketMessage
        {
            public string type;
            public string payload;
        }

        [System.Serializable]
        public class SceneSwitchPayload { public string scene; }
        [System.Serializable] public class DataSourceSwitchPayload { public string mode; }
        [System.Serializable] public class ParkingControlPayload { public string action; }
        [System.Serializable] public class VoiceCommandPayload { public string command; }
        [System.Serializable] public class SceneCurrentPayload { public string scene; }

        [System.Serializable]
        public class VehicleStatePayload
        {
            public int speed;
            public string gear;
            public string mode;
            public int frontDistance;
            public bool hasPedestrian;
            public bool hasObstacle;
            public bool laneDeparture;
            public int warningLevel;
        }

        [System.Serializable]
        public class ParkingStatePayload
        {
            public double front;
            public double rear;
            public double left;
            public double right;
            public string status;
        }

        [System.Serializable]
        public class RiskWarningPayload
        {
            public int level;
            public string message;
        }

        #region ========== 生命周期 ==========

        private void Start()
        {
            FindReferences();
            HookParkingController();
            Connect();
        }

        private void Update()
        {
            // 处理消息队列（主线程）
            lock (queueLock)
            {
                while (messageQueue.Count > 0)
                {
                    string msg = messageQueue.Dequeue();
                    ProcessMessage(msg);
                }
            }

            // 兜底：AutoParkingController 可能比本组件晚初始化
            if (!parkingHooked) HookParkingController();

            // 定期发送状态（每秒5次）
            // if (isConnected && Time.frameCount % 12 == 0)
            // {
            //     SendVehicleState();
            // }
        }

        /// <summary>
        /// 接上 AutoParkingController 的完成/失败回调（幂等，重复调用无副作用）
        /// </summary>
        private void HookParkingController()
        {
            if (parkingHooked) return;

            if (parkingController == null)
                parkingController = FindObjectOfType<AutoParkingController>();
            if (parkingController == null) return;

            parkingController.OnParkingComplete -= HandleParkingComplete;
            parkingController.OnParkingComplete += HandleParkingComplete;
            parkingController.OnParkingError -= HandleParkingError;
            parkingController.OnParkingError += HandleParkingError;

            parkingHooked = true;
            Debug.Log("[WebSocket] 已接上泊车完成回调，完成后会上报 parking.state=completed");
        }

        private void HandleParkingComplete()
        {
            Debug.Log("[WebSocket] 泊车完成 → 上报 parking.state = completed");
            ReportParkingState("completed");
        }

        private void HandleParkingError(string message)
        {
            Debug.LogWarning("[WebSocket] 泊车上报错误: " + message + " → 上报 parking.state = idle");
            ReportParkingState("idle");
        }
        /// <summary>
        /// 查找场景引用
        /// </summary>
        private void FindReferences()
        {
            if (viewController == null)
                viewController = FindObjectOfType<ViewSwitchController>();
            if (obstacleManager == null)
                obstacleManager = FindObjectOfType<ObstacleManager>();
            if (visualEnhancer == null)
                visualEnhancer = FindObjectOfType<ObstacleVisualEnhancer>();
            // ✅ 新增：自动查找 LaneLineManager
            if (laneLineManager == null)
                laneLineManager = FindObjectOfType<LaneLineManager>();
        }

        private void OnDestroy()
        {
            CloseWebSocket();
        }

        private void OnApplicationQuit()
        {
            CloseWebSocket();
        }
        #endregion
        #region ========== 连接管理 ==========

        public async void Connect()
        {
            if (isConnected) return;

            try
            {
                cts = new CancellationTokenSource();
                websocket = new ClientWebSocket();

                Debug.Log($"[WebSocket] 正在连接: {serverUrl}");
                await websocket.ConnectAsync(new System.Uri(serverUrl), cts.Token);

                if (websocket.State == WebSocketState.Open)
                {
                    isConnected = true;
                    isReconnecting = false;
                    Debug.Log($"[WebSocket] 已连接");

                    SendSceneCurrent();

                    // 启动接收循环（纯异步，不是协程）
                    _ = ReceiveLoopAsync();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WebSocket] 连接失败: {e.Message}");
                StartCoroutine(ReconnectCoroutine());
            }
        }

        private IEnumerator ReconnectCoroutine()
        {
            if (isReconnecting) yield break;
            isReconnecting = true;

            while (!isConnected)
            {
                Debug.Log($"[WebSocket] {reconnectInterval}秒后重连...");
                yield return new WaitForSeconds(reconnectInterval);
                Connect();
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void CloseWebSocket()
        {
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                cts?.Cancel();
                try
                {
                    websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { }
                websocket.Dispose();
            }
            isConnected = false;
        }
        #endregion
        #region ========== 接收循环（纯异步，不用IEnumerator） ==========

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[4096];

            while (isConnected && websocket != null && websocket.State == WebSocketState.Open)
            {
                try
                {
                    var segment = new System.ArraySegment<byte>(buffer);
                    var result = await websocket.ReceiveAsync(segment, cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[WebSocket] 服务端要求关闭");
                        isConnected = false;
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        lock (queueLock)
                        {
                            messageQueue.Enqueue(msg);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[WebSocket] 接收异常: {e.Message}");
                    isConnected = false;
                    break;
                }
            }

            // 断开后尝试重连
            if (!isConnected && !isReconnecting)
            {
                StartCoroutine(ReconnectCoroutine());
            }
        }
        #endregion
        #region========== 消息处理 ==========

        private void ProcessMessage(string json)
        {
            try
            {
                // ✅ 用 JObject 直接解析（不需要中间类了）
                JObject root = JObject.Parse(json);
                string type = root["type"]?.ToString();

                if (string.IsNullOrEmpty(type))
                {
                    Debug.LogWarning($"[WebSocket] 无效消息: {json}");
                    return;
                }

                Debug.Log($"[WebSocket] 收到: {type}");

                // ✅ payload 直接作为 JObject
                JObject payload = root["payload"] as JObject;


                ///此处按照信息的种类进行处理
                switch (type)
                {
                    // 旧协议（保留兼容）
                    case "scene.switch": HandleSceneSwitch(payload); break;
                    case "data.source.switch": HandleDataSourceSwitch(payload); break;
                    case "parking.control": HandleParkingControl(payload); break;
                    case "voice.command": HandleVoiceCommand(payload); break;

                    // 新协议
                    case "hmi.web.snapshot": HandleSnapshot(payload); break;
                    case "hmi.web.signal": HandleSignal(payload); break;
                    case "web.data.snapshot": HandleSnapshot(payload); break;
                    case "auto.request": HandleAutoRequest(payload); break;
                    //26 Add
                    case "road.info":HandleRoadInfo(payload); break;
                    default:
                        Debug.Log($"[WebSocket] 收到未处理类型: {type}");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WebSocket] 处理异常: {e.Message}\n原始消息: {json}");
            }
        }
        #endregion
        #region  ========== 消息处理器 ==========
        /// <summary>
        /// 处理 Android 发来的自动请求控制消息
        /// </summary>
        private void HandleRoadInfo(JObject payload)
        {
            if(payload==null)return;

            //RoadClass temp=new RoadClass;
            //roadRenderManager.SetInfo();
        }
        private void HandleAutoRequest(JObject payload)
        {
            if (payload == null) return;

            string action = payload["action"]?.ToString();
            Debug.Log($"[WebSocket] 自动请求控制: {action}");

            // 查找 AutoObstacleRequester
            AutoObstacleRequester requester = FindObjectOfType<AutoObstacleRequester>();

            if (requester == null)
            {
                Debug.LogWarning("[WebSocket] 未找到 AutoObstacleRequester");
                return;
            }

            switch (action)
            {
                case "start":
                    requester.StartRequesting();
                    break;
                case "stop":
                    requester.StopRequesting();
                    break;
                default:
                    Debug.Log($"[WebSocket] 未知的自动请求动作: {action}");
                    break;
            }
        }


        private void HandleSceneSwitch(JObject payload)
        {
            if (payload == null || viewController == null) return;

            string scene = payload["scene"]?.ToString();
            SceneSwitchController sceneController = FindObjectOfType<SceneSwitchController>();

            if (scene == "park")
            {
                // ✅ 只切换场景，不开始泊车
                if (sceneController != null)
                {
                    sceneController.SwitchToPark();
                }
                else
                {
                    viewController.SetView(ViewMode.Park);
                    Debug.LogWarning("[WebSocket] 未找到 SceneSwitchController，只切换了视角");
                }
            }
            else if (scene == "drive")
            {
                if (sceneController != null)
                {
                    sceneController.SwitchToDrive();
                }
                else
                {
                    viewController.SetView(ViewMode.Drive);
                }
            }

            SendSceneCurrent();
        }

        private void HandleDataSourceSwitch(JObject payload)
        {
            string mode = payload?["mode"]?.ToString();
            Debug.Log($"[WebSocket] 切换数据源: {mode}");
        }

        private void HandleParkingControl(JObject payload)
        {
            if (payload == null) return;

            string action = payload["action"]?.ToString();
            Debug.Log($"[WebSocket] 泊车控制: {action}");

            // ✅ 查找场景切换控制器
            SceneSwitchController sceneController = FindObjectOfType<SceneSwitchController>();

            switch (action)
            {
                case "start":
                    // ✅ 开始泊车（使用默认速度，或从 payload 读取）
                    float speed = payload["speed"]?.Value<float>() ?? 10f;
                    HookParkingController();
                    if (sceneController != null)
                    {
                        sceneController.StartParking(speed);
                    }
                    else
                    {
                        Debug.LogWarning("[WebSocket] 未找到 SceneSwitchController");
                    }

                    // 真的动起来了才报 running；否则报 idle，让仪表保持 0
                    AutoParkingController pc = parkingController != null
                        ? parkingController
                        : FindObjectOfType<AutoParkingController>();
                    if (pc != null && pc.isParking)
                    {
                        Debug.Log("[WebSocket] 泊车开始 → 上报 parking.state = running");
                        ReportParkingState("running");
                    }
                    else
                    {
                        ReportParkingState("idle");
                    }
                    break;

                case "pause":
                case "stop":
                    // 暂停泊车：真的停下来，并让仪表归零
                    AutoParkingController parking = parkingController != null
                        ? parkingController
                        : FindObjectOfType<AutoParkingController>();
                    if (parking != null) parking.StopParking();
                    ReportParkingState("idle");
                    break;

                case "reset":
                    // 进入泊车页时 App 会下发 reset：车回到起点，仪表归零
                    AutoParkingController resetter = parkingController != null
                        ? parkingController
                        : FindObjectOfType<AutoParkingController>();
                    if (resetter != null) resetter.ResetParking();
                    ReportParkingState("idle");
                    break;

                case "exit":
                    // 退出泊车
                    if (sceneController != null)
                    {
                        sceneController.SwitchToDrive();
                    }
                    ReportParkingState("idle");
                    break;

                default:
                    Debug.Log($"[WebSocket] 未知泊车动作: {action}");
                    break;
            }
        }

        private void HandleVoiceCommand(JObject payload)
        {
            string command = payload?["command"]?.ToString();
            Debug.Log($"[WebSocket] 语音指令: {command}");

            if (command == "open_drive")
                HandleSceneSwitch(JObject.Parse("{\"scene\":\"drive\"}"));
            else if (command == "open_park")
                HandleSceneSwitch(JObject.Parse("{\"scene\":\"park\"}"));
        }

        /// <summary>
        /// 处理快照消息 hmi.web.snapshot
        /// 读取 adas_status、obstacle_json、lane_json
        /// </summary>
        private void HandleSnapshot(JObject payload)
        {
            try
            {
                if (payload == null)
                {
                    Debug.LogError("[WebSocket] Snapshot payload 为 null");
                    return;
                }

                JObject processed = payload["processed"] as JObject;
                if (processed == null)
                {
                    Debug.LogError("[WebSocket] Snapshot 缺少 processed 字段");
                    return;
                }

                // 1. ADAS 状态
                int adasStatus = processed["adas_status"]?.Value<int>() ?? 0;
                if (obstacleManager != null)
                {
                    obstacleManager.SetAllActive(adasStatus == 1);
                }

                // ✅ 2. 车速 + 挡位：驱动车辆移动
                int speed = processed["speed"]?.Value<int>() ?? 0;
                string gear = processed["gear"]?.ToString() ?? "P";

                // ✅ 方向盘转角：优先用 steer_angle（-100~100，负左正右），
                //    没有 steer_angle 时回退到 left_click / right_click 按键模拟
                float steer = 0f;
                if (processed["steer_angle"] != null)
                {
                    float steerAngle = processed["steer_angle"].Value<float>();
                    steer = Mathf.Clamp(steerAngle / 100f, -1f, 1f);
                }
                else
                {
                    int leftClick = processed["left_click"]?.Value<int>() ?? 0;
                    int rightClick = processed["right_click"]?.Value<int>() ?? 0;
                    if (leftClick > 0) steer -= 1f;
                    if (rightClick > 0) steer += 1f;
                }

                // 首次调用或未赋值时，查找车辆速度控制器
                if (carController == null)
                {
                    carController = FindObjectOfType<CarWASDControl>();
                }
                if (carController != null)
                {
                    carController.SetGear(gear);
                    carController.SetSpeed(speed);
                    carController.SetSteer(steer);
                }

                // ✅ 把车速传给 AutoObstacleRequester（控制障碍物请求频率）
                AutoObstacleRequester requester = FindObjectOfType<AutoObstacleRequester>();
                if (requester != null)
                {
                    requester.UpdateSpeed(speed);
                }

                // ✅ 2. 从 signals 读取障碍物数据
                JObject signals = payload["signals"] as JObject;
                if (signals != null)
                {
                    // 障碍物数据
                    string obstacleJson = signals["Adas@ObstacleJsonData"]?.ToString();
                    if (obstacleManager != null && !string.IsNullOrEmpty(obstacleJson) && obstacleJson != "[]")
                    {
                        Debug.Log($"[WebSocket] 从 signals 提取障碍物数据，长度: {obstacleJson.Length}");
                        obstacleManager.RefreshByJson(obstacleJson);
                    }
                    else
                    {
                        Debug.Log($"[WebSocket] 无障碍物数据 (obstacleJson: {(string.IsNullOrEmpty(obstacleJson) ? "空" : obstacleJson)})");
                    }

                    // 车道线数据
                    string laneJson = signals["Adas@LaneLineJsonData"]?.ToString();
                    if (laneLineManager != null && !string.IsNullOrEmpty(laneJson) && laneJson != "[]")
                    {
                        Debug.Log($"[WebSocket] 从 signals 提取车道线数据，长度: {laneJson.Length}");
                        laneLineManager.RefreshLaneLines(laneJson);
                    }
                    else
                    {
                        Debug.Log($"[WebSocket] 无车道线数据 (laneJson: {(string.IsNullOrEmpty(laneJson) ? "空" : laneJson)})");
                    }
                }
                else
                {
                    Debug.LogWarning("[WebSocket] signals 字段为空");
                }

                Debug.Log($"[WebSocket] 快照: adas_status={adasStatus}, speed={speed}, gear={gear}, steer={steer:F2}, carController={(carController == null ? "NULL" : "OK")}");
                Debug.Log($"[WebSocket] processed keys: {string.Join(",", ((IDictionary<string, JToken>)processed).Keys)}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WebSocket] 解析snapshot失败: {e.Message}");
            }
        }

        /// <summary>
        /// 处理单个信号 hmi.web.signal
        /// </summary>
        private void HandleSignal(JObject payload)
        {
            try
            {
                if (payload == null)
                {
                    Debug.LogError("[WebSocket] Signal payload 为 null");
                    return;
                }

                string name = payload["name"]?.ToString();
                string value = payload["value"]?.ToString();
                string valueType = payload["valueType"]?.ToString();

                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError("[WebSocket] 信号缺少 name 字段");
                    return;
                }

                Debug.Log($"[WebSocket] 信号: {name} = {value} (type:{valueType})");

                switch (name)
                {
                    case "Adas@ObstacleJsonData":
                        if (obstacleManager != null && !string.IsNullOrEmpty(value))
                        {
                            Debug.Log($"[WebSocket] 准备传给 ObstacleManager: {value}");
                            obstacleManager.RefreshByJson(value);
                        }
                        break;

                    case "Adas@Status":
                        if (obstacleManager != null)
                        {
                            int status = value == "1" || value == "true" ? 1 : 0;
                            obstacleManager.SetAllActive(status == 1);
                        }
                        break;

                    case "Adas@AEB_Off":
                        Debug.Log($"[WebSocket] 收到AEB信号: {value}");
                        break;

                    case "Adas@LaneLineJsonData":
                        string laneValue = payload["value"]?.ToString();  // 用索引器访问
                        if (laneLineManager != null && !string.IsNullOrEmpty(laneValue))
                        {
                            laneLineManager.RefreshLaneLines(laneValue);
                        }
                        break;
                    default:
                        Debug.Log($"[WebSocket] 未处理的信号: {name}");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WebSocket] 解析signal失败: {e.Message}");
            }
        }
        #endregion
        #region========== 发送方法 ==========

        public void SendSceneCurrent()
        {
            if (!isConnected) return;
            string scene = (viewController != null && viewController.CurrentMode == ViewMode.Park)
                ? "park" : "drive";
            SendMessage("scene.current", new SceneCurrentPayload { scene = scene });
        }

        public void SendVehicleState()
        {
            if (!isConnected) return;

            var payload = new VehicleStatePayload
            {
                speed = GetCurrentSpeed(),
                gear = "D",
                mode = "ACC",
                frontDistance = GetFrontDistance(),
                hasPedestrian = false,
                hasObstacle = obstacleManager != null && obstacleManager.GetObstacleCount() > 0,
                laneDeparture = false,
                warningLevel = GetWarningLevel()
            };
            SendMessage("vehicle.state", payload);
        }

        public void SendParkingState(float front, float rear, float left, float right, string status)
        {
            if (!isConnected) return;
            var payload = new ParkingStatePayload
            {
                front = front,
                rear = rear,
                left = left,
                right = right,
                status = status
            };
            SendMessage("parking.state", payload);
        }

        /// <summary>
        /// 计算车辆四边到车位边界的余量，并上报泊车状态。
        /// 距离为真实几何余量：泊车到位时四边都接近 0，安卓端泊车页的四个距离才有意义。
        /// </summary>
        private void ReportParkingState(string status)
        {
            float front = 0f, rear = 0f, left = 0f, right = 0f;

            AutoParkingController pc = parkingController != null
                ? parkingController
                : FindObjectOfType<AutoParkingController>();

            if (pc != null && pc.parkingTarget != null)
            {
                // 车辆中心转换到车位坐标系
                Vector3 local = pc.parkingTarget.InverseTransformPoint(pc.transform.position);

                float halfDepth = pc.parkingDepth * 0.5f;
                float halfWidth = pc.parkingWidth * 0.5f;
                float halfCarLen = pc.carLength * 0.5f;
                float halfCarWid = pc.carWidth * 0.5f;

                // 车头朝 +z：车位前沿余量 / 后沿余量
                front = Mathf.Max(0f, halfDepth - local.z - halfCarLen);
                rear = Mathf.Max(0f, halfDepth + local.z - halfCarLen);
                left = Mathf.Max(0f, halfWidth + local.x - halfCarWid);
                right = Mathf.Max(0f, halfWidth - local.x - halfCarWid);
            }

            Debug.Log($"[WebSocket] parking.state={status} 前{front:F2} 后{rear:F2} 左{left:F2} 右{right:F2} (m)");
            SendParkingState(front, rear, left, right, status);
        }

        public void SendRiskWarning(int level, string message)
        {
            if (!isConnected) return;
            SendMessage("risk.warning", new RiskWarningPayload { level = level, message = message });
        }

        /// <summary>
        /// 通过Json文件传输到指定IP
        /// </summary>
        /// <param name="type"></param>
        /// <param name="payload"></param>
        private async void SendMessage(string type, object payload)
        {
            if (!isConnected || websocket == null || websocket.State != WebSocketState.Open) return;

            try
            {
                string payloadJson = JsonUtility.ToJson(payload);
                string fullJson = $"{{\"type\":\"{type}\",\"payload\":{payloadJson}}}";

                byte[] bytes = Encoding.UTF8.GetBytes(fullJson);
                var segment = new System.ArraySegment<byte>(bytes);
                await websocket.SendAsync(segment, WebSocketMessageType.Text, true, cts.Token);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WebSocket] 发送失败: {e.Message}");
            }
        }

        // ========== 数据辅助 ==========

        private int GetCurrentSpeed()
        {
            if (carController != null)
                return Mathf.RoundToInt(carController.GetCurrentSpeedKmh());
            // 备用：找一下
            carController = FindObjectOfType<CarWASDControl>();
            if (carController != null)
                return Mathf.RoundToInt(carController.GetCurrentSpeedKmh());
            return 0;
        }
        private int GetFrontDistance() => Random.Range(10, 50);

        private int GetWarningLevel()
        {
            // TODO: 从 visualEnhancer 读取实际危险等级
            return 0;
        }
    }
    #endregion
}