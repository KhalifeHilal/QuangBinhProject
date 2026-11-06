using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GAMAGeometryExport : ConnectionWithGama
{
    protected ConnectionParameter parameters = null;

    // Optional: define a scale between GAMA and Unity for the location given
    public float GamaCRSCoefX = 1.0f;
    public float GamaCRSCoefY = 1.0f;
    public float GamaCRSOffsetX = 0.0f;
    public float GamaCRSOffsetY = 0.0f;

    private bool continueProcess = true;
    GameObject objectToSend;

    Dictionary<string, string> argsToSend = null;

    public void ManageGeometries(GameObject objectToSend_, string ip_, string port_, float x, float y, float ox, float oy)
    {
        objectToSend = objectToSend_;
        if (objectToSend == null) return;
        parameters = null;

        ip = ip_;
        port = port_;
        GamaCRSCoefX = x;
        GamaCRSCoefY = y;
        GamaCRSOffsetX = ox;
        GamaCRSOffsetY = oy;

        UnityGeometry ug = new UnityGeometry(objectToSend, new CoordinateConverter(10000, x, y, ox, oy));
        string message = ug.ToJSON();

        argsToSend = new Dictionary<string, string>
        {
            { "geoms", message }
        };

        socket = new WebSocket("ws://" + ip + ":" + port + "/");

        continueProcess = true;

        socket.OnOpen += HandleConnectionOpen;
        socket.OnMessage += HandleReceivedMessage;
        socket.OnClose += HandleConnectionClosed;

        // Per-message Compression extension disabled to save bandwidth.
        socket.Compression = CompressionMethod.None;

        socket.Connect();

        // Simple loop to wait for parameter data before exporting geometries.
        while (continueProcess)
        {
            if (parameters != null)
            {
                ExportGeoms();
                continueProcess = false;
            }
        }
    }

    void HandleConnectionClosed(object sender, CloseEventArgs e)
    {
        continueProcess = false;
    }

    void HandleConnectionOpen(object sender, EventArgs e)
    {
        string jsonStringId = new JObject
        {
            ["type"] = "connection",
            ["id"] = "geomexporter",
            ["heartbeat"] = "5000"
        }.ToString(Formatting.None);
        SendMessageToServer(jsonStringId, (success) => { });
        Debug.Log("ConnectionManager: Connection opened");
    }


    private void ExportGeoms()
    {
        Debug.Log("export Geom");
        if (parameters != null)
        {
            SendExecutableAsk("receive_geometries", argsToSend);
            continueProcess = false;
        }
    }

    void HandleServerMessageReceived(string firstKey, string content)
    {
        if (string.IsNullOrEmpty(content) || content.Equals("{}"))
            return;
        else if (content.Contains("precision"))
            firstKey = "precision";

        switch (firstKey)
        {
            // Handle general information about the simulation.
            case "precision":
                parameters = ConnectionParameter.CreateFromJSON(content);
                Debug.Log("Received parameter data");
                break;
            // Other cases for handling geometries can be added here.
        }
    }

    void HandleReceivedMessage(object sender, MessageEventArgs e)
    {
        if (e.IsText)
        {
            JObject root = JObject.Parse(e.Data);
            string type = (string)root["type"];

            if (type == "json_output")
            {
                JObject contents = root["contents"] as JObject;
                if (contents != null)
                {
                    string firstKey = null;
                    foreach (JProperty prop in contents.Properties())
                    {
                        firstKey = prop.Name;
                        break;
                    }
                    HandleServerMessageReceived(firstKey, contents.ToString(Formatting.None));
                }
            }
            else if (type == "json_state")
            {
                bool inGameFlag = (bool?)root["in_game"] ?? false;
                if (inGameFlag)
                {
                    var args = new Dictionary<string, string> { { "id", "geomexporter" } };
                    SendExecutableAsk("send_init_data", args);
                }
            }
        }
    }
}
