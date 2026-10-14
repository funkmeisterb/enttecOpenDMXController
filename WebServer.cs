using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dmxEasyDancing
{
    public class WebServer
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Action<byte, byte, byte, byte, byte> _onColorChanged;
        private readonly Action<bool> _onPatternToggle;
        private readonly Action<bool> _onRandomEffectToggle;
        private readonly Func<AppState> _getState;
        private readonly int _port;
        private readonly byte[] _htmlBytes;

        public class AppState
        {
            public byte Gain { get; set; } = 255;
            public byte R { get; set; } = 255;
            public byte G { get; set; } = 0;
            public byte B { get; set; } = 0;
            public byte W { get; set; } = 0;
            public bool PatternRunning { get; set; } = false;
            public bool RandomEffectRunning { get; set; } = false;
        }

        public WebServer(
            int port,
            Action<byte, byte, byte, byte, byte> onColorChanged,
            Action<bool> onPatternToggle,
            Action<bool> onRandomEffectToggle,
            Func<AppState> getState)
        {
            _port = port;
            _onColorChanged = onColorChanged;
            _onPatternToggle = onPatternToggle;
            _onRandomEffectToggle = onRandomEffectToggle;
            _getState = getState;
            _htmlBytes = LoadHtmlBytes();
        }

        private static byte[] LoadHtmlBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("enttecOpenDMXController.index.html")
                ?? (File.Exists("index.html") ? File.OpenRead("index.html") : null);

            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            return Encoding.UTF8.GetBytes("<h1>index.html not found</h1>");
        }

        public void Start()
        {
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            Task.Run(ListenLoop);
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            foreach (var kvp in _clients)
            {
                try { kvp.Value.Abort(); } catch { }
            }
        }
        private async Task ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = ProcessWebSocketRequest(context);
                    }
                    else
                    {
                        ProcessHttpRequest(context);
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebServer error] {ex.Message}");
                }
            }
        }

        private void ProcessHttpRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "GET")
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = _htmlBytes.Length;
                    context.Response.OutputStream.Write(_htmlBytes, 0, _htmlBytes.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            catch { }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private async Task ProcessWebSocketRequest(HttpListenerContext context)
        {
            WebSocketContext wsContext;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(null);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                Console.WriteLine($"[WebSocket Accept error]: {ex.Message}");
                return;
            }

            var id = Guid.NewGuid();
            var ws = wsContext.WebSocket;
            _clients[id] = ws;

            await SendStateAsync(ws, _getState());

            var buffer = new byte[4096];
            try
            {
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        HandleClientMessage(json);
                    }
                }
            }
            catch { }
            finally
            {
                _clients.TryRemove(id, out _);
                try { ws.Dispose(); } catch { }
            }
        }

        private void HandleClientMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) return;

                string type = typeEl.GetString() ?? "";
                if (type == "color")
                {
                    byte gain = root.GetProperty("gain").GetByte();
                    byte r = root.GetProperty("r").GetByte();
                    byte g = root.GetProperty("g").GetByte();
                    byte b = root.GetProperty("b").GetByte();
                    byte w = root.GetProperty("w").GetByte();

                    _onColorChanged(gain, r, g, b, w);
                    BroadcastState(_getState());
                }
                else if (type == "pattern")
                {
                    bool enabled = root.GetProperty("enabled").GetBoolean();
                    _onPatternToggle(enabled);
                    BroadcastState(_getState());
                }
                else if (type == "randomEffect")
                {
                    bool enabled = root.GetProperty("enabled").GetBoolean();
                    _onRandomEffectToggle(enabled);
                    BroadcastState(_getState());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Message Parse error]: {ex.Message}");
            }
        }

        public void BroadcastState(AppState state)
        {
            foreach (var kvp in _clients)
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    _ = SendStateAsync(kvp.Value, state);
                }
            }
        }

        private static async Task SendStateAsync(WebSocket ws, AppState state)
        {
            try
            {
                var payload = new
                {
                    type = "state",
                    gain = state.Gain,
                    r = state.R,
                    g = state.G,
                    b = state.B,
                    w = state.W,
                    pattern = state.PatternRunning,
                    randomEffect = state.RandomEffectRunning
                };
                string json = JsonSerializer.Serialize(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

    }
}
