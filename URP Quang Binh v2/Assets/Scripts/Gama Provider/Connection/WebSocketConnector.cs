using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;


public abstract class WebSocketConnector : MonoBehaviour
{ /*192.168.68.50*/
    [SerializeField] string DefaultIP = "localhost";
    protected string DefaultPort = "8080";

    protected string host;
    protected string port;

    protected bool UseMiddleware;

    private WebSocket socket;
    readonly Queue<Action> pendingCallbacks = new Queue<Action>();
    readonly object callbackLock = new object();
    bool shuttingDown;


    protected int HeartbeatInMs = 5000; //only for middleware mode
    protected bool DesktopMode = false;
    [Tooltip("Select true if you're using localhost or a fixed IP")] public bool fixedProperties = true;
    protected bool UseMiddlewareDM = true;

    protected int numErrorsBeforeDeconnection = 10;
    protected int numErrors = 0;

    void OnEnable()
    {
        // port = PlayerPrefs.GetString("PORT"); 
        host = PlayerPrefs.GetString("IP");
        port = DefaultPort;
        UseMiddleware = UseMiddlewareDM;

        if (DesktopMode)
        {
            
            host = "localhost";

            if (UseMiddleware)
            {
                port = "8080";
            }
            else
            {
                port = "1000";
            }
        }
        else if (fixedProperties)
        {
            host = DefaultIP;
            port = DefaultPort;
        }
        else
        {
            Debug.Log("current IP: " + host);
            if (string.IsNullOrEmpty(host) || ValidIp(host))
            {
                host = DefaultIP;
                Debug.Log("IP not set, using default: " + host);
            }
        }

        Debug.Log("WebSocketConnector host: " + host + " PORT: " + port + " MIDDLEWARE:" + UseMiddleware);

        socket = new WebSocket("ws://" + host + ":" + port + "/");

        // Enable the Per-message Compression extension.
        // Saved some bandwidth
        // Doesn't work on our specific installation : https://github.com/sta/websocket-sharp/issues/580
        socket.Compression = CompressionMethod.None; //Deflate;

        socket.OnOpen += QueueConnectionOpen;
        socket.OnMessage += QueueReceivedMessage;
        socket.OnClose += QueueConnectionClosed;
    }

    // WebSocketSharp invokes its callbacks on worker threads. Dispatch them
    // here before handlers or subscribers access Unity objects/coroutines.
    void Update()
    {
        int count;
        lock (callbackLock)
            count = pendingCallbacks.Count;

        for (int i = 0; i < count; i++)
        {
            Action callback;
            lock (callbackLock)
            {
                if (shuttingDown || pendingCallbacks.Count == 0)
                    return;
                callback = pendingCallbacks.Dequeue();
            }

            try { callback(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    void QueueCallback(Action callback)
    {
        lock (callbackLock)
        {
            if (!shuttingDown)
                pendingCallbacks.Enqueue(callback);
        }
    }

    void QueueConnectionOpen(object sender, EventArgs e)
    {
        QueueCallback(() => HandleConnectionOpen(sender, e));
    }

    void QueueReceivedMessage(object sender, MessageEventArgs e)
    {
        QueueCallback(() => HandleReceivedMessage(sender, e));
    }

    void QueueConnectionClosed(object sender, CloseEventArgs e)
    {
        QueueCallback(() => HandleConnectionClosed(sender, e));
    }

    void OnDestroy()
    {
        lock (callbackLock)
        {
            shuttingDown = true;
            pendingCallbacks.Clear();
        }

        if (socket == null) return;
        socket.OnOpen -= QueueConnectionOpen;
        socket.OnMessage -= QueueReceivedMessage;
        socket.OnClose -= QueueConnectionClosed;
        socket.CloseAsync();
    }

    // ############################## HANDLERS ##############################

    protected abstract void HandleConnectionOpen(object sender, System.EventArgs e);

    protected abstract void HandleReceivedMessage(object sender, MessageEventArgs e);

    protected abstract void HandleConnectionClosed(object sender, CloseEventArgs e);

    // #######################################################################

    protected void SendMessageToServer(string message, Action<bool> successCallback)
    {
        socket.SendAsync(message, success =>
        {
            if (successCallback != null)
                QueueCallback(() => successCallback(success));
        });
    }

    protected WebSocket GetSocket()
    {
        return socket;
    }

    private bool ValidIp(string ip)
    {
        if (ip == null || ip.Length == 0) return false;
        string[] ipb = ip.Split(".");
        return (ipb.Length != 4);
    }

    public void SetNewIP(string ip)
    {
        DefaultIP = ip;
    }
}
