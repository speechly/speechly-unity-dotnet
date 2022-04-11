using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Text;

namespace Speechly.SLUClient {

  public class CloudDecoder: IDecoder {
    public event ResponseReceivedDelegate OnMessage = (MsgCommon msgCommon, string msgString) => {};
    private string loginUrl = "https://api.speechly.com/login";
    private string apiUrl = "wss://api.speechly.com/ws/v1?sampleRate=16000";
    private string projectId = null;
    private string appId = null;
    private string deviceId = null;
    private bool debug = false;

    private WsClient wsClient = null;
    private ConcurrentQueue<TaskCompletionSource<string>> startContextTCS = new ConcurrentQueue<TaskCompletionSource<string>>();
    private ConcurrentQueue<TaskCompletionSource<string>> stopContextTCS = new ConcurrentQueue<TaskCompletionSource<string>>();

    public CloudDecoder(
      string deviceId,
      string loginUrl = null,
      string apiUrl = null,
      string projectId = null,
      string appId = null,
      bool debug = false
    ) {
      if (loginUrl != null) this.loginUrl = loginUrl;
      if (apiUrl != null) this.apiUrl = apiUrl;
      if (projectId != null) this.projectId = projectId;
      if (appId != null) this.appId = appId;
      this.deviceId = deviceId;
      this.debug = debug;
    }

    public async Task Initialize() {
      if (debug) Logger.Log("Initializing and connecting Cloud SLU...");
      var tokenFetcher = new LoginToken();
      string token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

      if (debug) Logger.Log($"token: {token}");

      wsClient = new WsClient();
      wsClient.OnResponseReceived = OnResponse;

      await wsClient.ConnectAsync(apiUrl, token);
      if (debug) Logger.Log("Cloud SLU ready");
    }

    public Task<string> StartContext() {
      var tcs = new TaskCompletionSource<string>();
      startContextTCS.Enqueue(tcs);
      if (appId != null) {
        wsClient.SendText($"{{\"event\": \"start\", \"appId\": \"{appId}\"}}");
      } else {
        wsClient.SendText($"{{\"event\": \"start\"}}");
      }
      return tcs.Task;
    }

    private void OnResponse(MemoryStream inputStream)
    {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      try {
        // @TODO Find a way to deserialize only once
        var msgCommon = JSON.Parse(msgString, new MsgCommon());
        OnMessage(msgCommon, msgString);
        switch (msgCommon.type) {
          case "started": {
            TaskCompletionSource<string> tcs;
            startContextTCS.TryDequeue(out tcs);
            tcs.SetResult(msgCommon.audio_context);
            break;
          }
          case "stopped": {
            TaskCompletionSource<string> tcs;
            stopContextTCS.TryDequeue(out tcs);
            tcs.SetResult(msgCommon.audio_context);
            break;
          }
        }
      } catch {
        Logger.LogError($"Error while handling message with content: {msgString}");
        throw;
      }
    }

    public void SendAudio(float[] floats, int start = 0, int length = -1) {
      if (length < 0) length = floats.Length;
      int end = start + length;
      // @TODO Use a pre-allocated buf
      var buf = new byte[length * 2];
      int i = 0;

      for (var l = start; l < end; l++) {
        short v = (short)(floats[l] * 0x7fff);
        buf[i++] = (byte)(v);
        buf[i++] = (byte)(v >> 8);
      }

      wsClient.SendBytes(new ArraySegment<byte>(buf));
    }

    public Task<string> StopContext() {
      var tcs = new TaskCompletionSource<string>();
      stopContextTCS.Enqueue(tcs);
      wsClient.SendText($"{{\"event\": \"stop\"}}");
      return tcs.Task;
    }

    public async Task Shutdown() {
      if (debug) Logger.Log("Cloud SLU shutting down...");
      await wsClient.DisconnectAsync();
      if (debug) Logger.Log("Shutdown completed.");
    }

  }
}