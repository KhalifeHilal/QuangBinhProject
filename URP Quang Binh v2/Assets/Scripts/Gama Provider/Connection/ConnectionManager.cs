using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;
using System.Collections;

public class ConnectionManager : WebSocketConnector
{
    private ConnectionState currentState;
    private bool connectionRequested;

    // called when the connection state is manually changed
    public event Action<ConnectionState> OnConnectionStateChanged;

    // called when a "json_simulation" message is received
    public event Action<string, string> OnServerMessageReceived;

    // called when a "json_state" message is received 
    public event Action<JObject> OnConnectionStateReceived;

    // called when a connection request fails
    public event Action<bool> OnConnectionAttempted;

    public static ConnectionManager Instance = null;

    // use to separate messages in the case where the middleware is not used
    protected string MessageSeparator = "|||";

    private string AgentToSendInfo = "simulation[0].unity_linker[0]";

    public float connectionTimeout = 10f;

    Coroutine timeoutCoroutine;

    // ############################################# UNITY FUNCTIONS #############################################
    void Awake()
    {
        UseMiddleware = DesktopMode ? UseMiddlewareDM : PlayerPrefs.GetString("MIDDLEWARE").Equals("Y");
        Debug.Log("ConnectionManager: Awake : " + PlayerPrefs.GetString("MIDDLEWARE"));
        Debug.Log("ConnectionManager Awake host: " + PlayerPrefs.GetString("IP") + " PORT: " +
                  PlayerPrefs.GetString("PORT") + " UseMiddleware: " + UseMiddleware);

        Instance = this;
    }

    void Start()
    {
        Debug.Log("START");
        UpdateConnectionState(ConnectionState.DISCONNECTED);
        connectionRequested = false;
    }

    public string GetMessageSeparator()
    {
        return MessageSeparator;
    }

    // ############################################# CONNECTION HANDLER #############################################
    public void UpdateConnectionState(ConnectionState newState)
    {
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }

        switch (newState)
        {
            case ConnectionState.PENDING:
                Debug.Log("ConnectionManager: UpdateConnectionState -> PENDING");
                timeoutCoroutine = StartCoroutine(TimeoutCoroutine());
                break;
            case ConnectionState.CONNECTED:
                Debug.Log("ConnectionManager: UpdateConnectionState -> CONNECTED");
                break;
            case ConnectionState.AUTHENTICATED:
                Debug.Log("ConnectionManager: UpdateConnectionState -> AUTHENTICATED");
                break;
            case ConnectionState.DISCONNECTED:
                Debug.Log("ConnectionManager: UpdateConnectionState -> DISCONNECTED");
                TryConnectionToServer();
                break;
            default:
                break;
        }

        currentState = newState;
        OnConnectionStateChanged?.Invoke(newState);
    }

    // ############################################# HANDLERS #############################################

    protected override void HandleConnectionOpen(object sender, EventArgs e)
    {
        if (UseMiddleware)
        {
            string jsonStringId = new JObject
            {
                ["type"] = "connection",
                ["id"] = StaticInformation.getId(),
                ["heartbeat"] = HeartbeatInMs
            }.ToString(Formatting.None);

            SendMessageToServer(jsonStringId, new Action<bool>((success) =>
            {
                Debug.Log("ConnectionManager: HandleConnectionOpen -> " + jsonStringId);
            }));
            Debug.Log("ConnectionManager: Connection opened");
        }
    }

    protected override void HandleReceivedMessage(object sender, MessageEventArgs e)
    {
        if (e.IsText)
        {
            JObject root = JObject.Parse(e.Data);
            string type = (string)root["type"];

            if (UseMiddleware)
            {
                switch (type)
                {
                    case "ping":
                        string jsonPong = new JObject { ["type"] = "pong" }.ToString(Formatting.None);
                        SendMessageToServer(jsonPong, new Action<bool>((success) =>
                        {
                            // Optionally handle success.
                        }));
                        break;
                    case "json_state":
                        OnConnectionStateReceived?.Invoke(root);
                        bool authenticated = (bool?)root["in_game"] ?? false;
                        bool connected = (bool?)root["connected"] ?? false;

                        if (authenticated && connected)
                        {
                            if (!IsConnectionState(ConnectionState.AUTHENTICATED))
                            {
                                Debug.Log("ConnectionManager: Player successfully authenticated");
                                UpdateConnectionState(ConnectionState.AUTHENTICATED);
                            }
                        }
                        else if (connected && !authenticated)
                        {
                            if (!IsConnectionState(ConnectionState.CONNECTED))
                            {
                                connectionRequested = false;
                                Debug.Log("ConnectionManager: Successfully connected, waiting for authentication...");
                                UpdateConnectionState(ConnectionState.CONNECTED);
                                OnConnectionAttempted?.Invoke(true);
                            }
                            else
                            {
                                Debug.LogWarning("ConnectionManager: Already connected, waiting for authentication...");
                            }
                        }
                        break;
                    case "json_output":
                        JObject content = root["contents"] as JObject;
                        string firstKey = null;
                        if (content != null)
                        {
                            foreach (JProperty prop in content.Properties())
                            {
                                firstKey = prop.Name;
                                break;
                            }
                        }
                        OnServerMessageReceived?.Invoke(firstKey, content?.ToString(Formatting.None));
                        break;
                    default:
                        break;
                }
            }
            else if (type.Equals("SimulationOutput"))
            {
                string content = (string)root["content"];
                // Split using the message separator.
                foreach (string mes in content.Split(new string[] { MessageSeparator }, StringSplitOptions.None))
                {
                    if (!string.IsNullOrEmpty(mes))
                        OnServerMessageReceived?.Invoke(null, mes);
                }
            }
        }
    }

    protected override void HandleConnectionClosed(object sender, CloseEventArgs e)
    {
        Debug.Log("ConnectionManager: HandleConnectionClosed");
        if (connectionRequested)
        {
            connectionRequested = false;
            OnConnectionAttempted?.Invoke(false);
            Debug.Log("ConnectionManager: Failed to connect to server");
        }

        UpdateConnectionState(ConnectionState.DISCONNECTED);
    }

    // ############################################# UTILITY FUNCTIONS #############################################
    public void TryConnectionToServer()
    {
        if (IsConnectionState(ConnectionState.DISCONNECTED))
        {
            Debug.Log("ConnectionManager: Attempting to connect to " + (UseMiddleware ? "middleware" : "GAMA") +
                      ": ws://" + host + ":" + port + "/");
            connectionRequested = true;
            UpdateConnectionState(ConnectionState.PENDING);

            GetSocket().ConnectAsync();

            if (!UseMiddleware)
            {
                Debug.Log("Create player direct :" + ConnectionManager.Instance.GetConnectionId());

                Dictionary<string, string> args = new Dictionary<string, string>
                {
                    { "id", "\"" + ConnectionManager.Instance.GetConnectionId() + "\"" }
                };
                SendExecutableAsk("create_init_player", args);

                UpdateConnectionState(ConnectionState.AUTHENTICATED);
            }
        }
        else
        {
            Debug.LogWarning("ConnectionManager: Already connected to middleware: " + this.currentState);
        }
    }

    public void DisconnectFromServer()
    {
        if (!IsConnectionState(ConnectionState.DISCONNECTED))
        {
            Debug.Log("ConnectionManager: Disconnecting from middleware...");
            GetSocket().CloseAsync();
            UpdateConnectionState(ConnectionState.DISCONNECTED);
        }
        else
        {
            Debug.LogWarning("ConnectionManager: Already disconnected from middleware");
        }
    }

    public bool IsConnectionState(ConnectionState state)
    {
        return this.currentState == state;
    }

    public void SendExecutableExpression(string expression)
    {
        string jsonStringExpression = new JObject
        {
            ["type"] = "expression",
            ["expr"] = expression
        }.ToString(Formatting.None);

        SendMessageToServer(jsonStringExpression, new Action<bool>((success) =>
        {
            if (!success)
            {
                numErrors++;
                Debug.LogError("ConnectionManager: Failed to send executable expression");
                if (numErrors > numErrorsBeforeDeconnection)
                {
                    GetSocket().Close();
                    currentState = ConnectionState.DISCONNECTED;
                    numErrors = 0;
                }
            }
            else
            {
                numErrors = 0;
            }
        }));
    }

    public void SendExecutableAsk(string action, Dictionary<string, string> arguments)
    {
        JObject args = new JObject();
        foreach (var kvp in arguments)
            args[kvp.Key] = kvp.Value;

        string jsonStringExpression = new JObject
        {
            ["type"] = "ask",
            ["action"] = action,
            ["args"] = args.ToString(Formatting.None),
            ["agent"] = AgentToSendInfo
        }.ToString(Formatting.None);

        SendMessageToServer(jsonStringExpression, new Action<bool>((success) =>
        {
            if (!success)
            {
                numErrors++;
                Debug.LogError("ConnectionManager: Failed to send executable ask");
                if (numErrors > numErrorsBeforeDeconnection)
                {
                    GetSocket().Close();
                    currentState = ConnectionState.DISCONNECTED;
                    numErrors = 0;
                }
            }
            else
            {
                numErrors = 0;
            }
        }));
    }

    public void DisconnectProperly()
    {
        string jsonStringExpression = new JObject
        {
            ["type"] = "disconnect_properly"
        }.ToString(Formatting.None);

        SendMessageToServer(jsonStringExpression, new Action<bool>((success) =>
        {
            if (!success)
            {
                Debug.LogError("ConnectionManager: Failed to send disconnect message");
            }
            else
            {
                DisconnectFromServer();
            }
        }));
    }

    public string GetConnectionId()
    {
        return StaticInformation.getId();
    }

    public bool getUseMiddleware()
    {
        return UseMiddleware;
    }

    public void Reconnect()
    {
        Debug.Log("Reconnect");
        currentState = ConnectionState.DISCONNECTED;
        TryConnectionToServer();
    }

    IEnumerator TimeoutCoroutine()
    {
        Debug.Log("ConnectionManager: Starting connection timeout coroutine for " + connectionTimeout + " seconds.");
        yield return new WaitForSeconds(connectionTimeout);
        Debug.Log("ConnectionManager: Connection attempt timed out.");
        DisconnectFromServer();
        SceneManager.LoadScene("IP Menu");
    }
}

public enum ConnectionState
{
    DISCONNECTED,
    PENDING,
    CONNECTED,
    AUTHENTICATED
}
