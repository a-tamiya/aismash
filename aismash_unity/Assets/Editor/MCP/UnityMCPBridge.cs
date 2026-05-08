using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiSmash.Editor.MCP
{
    [InitializeOnLoad]
    public static class UnityMCPBridge
    {
        private const int Port = 6400;

        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static readonly ConcurrentQueue<PendingRequest> _requestQueue = new();
        private static readonly List<string> _logBuffer = new();
        private static readonly object _logLock = new();

        private class PendingRequest
        {
            public string CommandJson;
            public string ResponseJson;
            public readonly ManualResetEventSlim Done = new(false);
        }

        static UnityMCPBridge()
        {
            Application.logMessageReceived += OnLog;
            EditorApplication.update += ProcessQueue;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
            StartServer();
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_logLock)
            {
                _logBuffer.Add($"[{type}] {message}");
                if (_logBuffer.Count > 500) _logBuffer.RemoveAt(0);
            }
        }

        private static void StartServer()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "UnityMCPBridge" };
                _listenerThread.Start();
                Debug.Log($"[MCP] Listening on port {Port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP] Failed to start server: {e.Message}");
            }
        }

        private static void StopServer()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= ProcessQueue;
            _listener?.Stop();
        }

        private static void ListenLoop()
        {
            while (true)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
                }
                catch { break; }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    var json = reader.ReadLine();
                    if (json == null) return;

                    var req = new PendingRequest { CommandJson = json };
                    _requestQueue.Enqueue(req);

                    if (!req.Done.Wait(TimeSpan.FromSeconds(15)))
                    {
                        writer.WriteLine("{\"success\":false,\"error\":\"timeout\"}");
                        return;
                    }

                    writer.WriteLine(req.ResponseJson);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP] Client handler error: {e.Message}");
            }
        }

        private static void ProcessQueue()
        {
            while (_requestQueue.TryDequeue(out var req))
            {
                try
                {
                    req.ResponseJson = Execute(req.CommandJson);
                }
                catch (Exception e)
                {
                    req.ResponseJson = Err(e.Message);
                }
                finally
                {
                    req.Done.Set();
                }
            }
        }

        private static string Execute(string json)
        {
            var cmd = JsonUtility.FromJson<McpCommand>(json);
            if (cmd == null) return Err("Invalid JSON");

            return cmd.type switch
            {
                "ping"                  => Ok("pong"),
                "get_project_info"      => GetProjectInfo(),
                "get_scene_hierarchy"   => GetSceneHierarchy(),
                "get_logs"              => GetLogs(cmd.count > 0 ? cmd.count : 50),
                "get_assets"            => GetAssets(cmd.filter),
                "execute_menu_item"     => ExecuteMenuItem(cmd.path),
                "refresh_assets"        => RefreshAssets(),
                _                       => Err($"Unknown command: {cmd.type}")
            };
        }

        // ---- Command handlers ----

        private static string GetProjectInfo()
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"result\":{");
            sb.Append($"\"productName\":{J(Application.productName)},");
            sb.Append($"\"unityVersion\":{J(Application.unityVersion)},");
            sb.Append($"\"dataPath\":{J(Application.dataPath)},");
            sb.Append($"\"platform\":{J(Application.platform.ToString())}");
            sb.Append("}}");
            return sb.ToString();
        }

        private static string GetSceneHierarchy()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"result\":{");
            sb.Append($"\"scene\":{J(scene.name)},");
            sb.Append("\"objects\":[");
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendGO(sb, roots[i], 0);
            }
            sb.Append("]}}");
            return sb.ToString();
        }

        private static void AppendGO(StringBuilder sb, GameObject go, int depth)
        {
            sb.Append("{");
            sb.Append($"\"name\":{J(go.name)},");
            sb.Append($"\"active\":{(go.activeSelf ? "true" : "false")},");
            var comps = go.GetComponents<Component>();
            sb.Append("\"components\":[");
            bool first = true;
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (!first) sb.Append(',');
                sb.Append(J(c.GetType().Name));
                first = false;
            }
            sb.Append("]");
            if (depth < 4 && go.transform.childCount > 0)
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendGO(sb, go.transform.GetChild(i).gameObject, depth + 1);
                }
                sb.Append("]");
            }
            sb.Append("}");
        }

        private static string GetLogs(int count)
        {
            lock (_logLock)
            {
                var start = Math.Max(0, _logBuffer.Count - count);
                var sb = new StringBuilder();
                sb.Append("{\"success\":true,\"result\":[");
                for (int i = start; i < _logBuffer.Count; i++)
                {
                    if (i > start) sb.Append(',');
                    sb.Append(J(_logBuffer[i]));
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        private static string GetAssets(string filter)
        {
            var guids = AssetDatabase.FindAssets(filter ?? "");
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"result\":[");
            int limit = Math.Min(guids.Length, 200);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(J(AssetDatabase.GUIDToAssetPath(guids[i])));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string ExecuteMenuItem(string path)
        {
            if (string.IsNullOrEmpty(path)) return Err("path is required");
            bool ok = EditorApplication.ExecuteMenuItem(path);
            return ok ? Ok($"Executed: {path}") : Err($"Menu item not found: {path}");
        }

        private static string RefreshAssets()
        {
            AssetDatabase.Refresh();
            return Ok("AssetDatabase refreshed");
        }

        // ---- Helpers ----

        private static string Ok(string result) => $"{{\"success\":true,\"result\":{J(result)}}}";
        private static string Err(string msg)    => $"{{\"success\":false,\"error\":{J(msg)}}}";

        private static string J(string s)
        {
            if (s == null) return "null";
            return "\"" + s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                + "\"";
        }

        [Serializable]
        private class McpCommand
        {
            public string type;
            public string path;
            public string filter;
            public int    count;
        }
    }
}
