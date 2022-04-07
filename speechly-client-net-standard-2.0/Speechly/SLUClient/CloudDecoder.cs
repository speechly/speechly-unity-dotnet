using System;
using System.IO;
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
    private bool debug = true;

    private WsClient wsClient = null;
    private TaskCompletionSource<MsgCommon> startContextTCS;
    private TaskCompletionSource<MsgCommon> stopContextTCS;

    public CloudDecoder(
      string deviceId,
      string loginUrl = null,
      string apiUrl = null,
      string projectId = null,
      string appId = null
    ) {
      if (loginUrl != null) this.loginUrl = loginUrl;
      if (apiUrl != null) this.apiUrl = apiUrl;
      if (projectId != null) this.projectId = projectId;
      if (appId != null) this.appId = appId;
      this.deviceId = deviceId;
    }

    public async Task Initialize() {
      var tokenFetcher = new LoginToken();
      string token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

      if (debug) Logger.Log($"token: {token}");

      wsClient = new WsClient();
      wsClient.OnResponseReceived = OnResponse;

      await wsClient.ConnectAsync(apiUrl, token);
    }

    public async Task<string> StartContext() {
      startContextTCS = new TaskCompletionSource<MsgCommon>();
      Task t;
      if (appId != null) {
        t = wsClient.SendText($"{{\"event\": \"start\", \"appId\": \"{appId}\"}}");
      } else {
        t = wsClient.SendText($"{{\"event\": \"start\"}}");
      }
      await t;
      MsgCommon msgCommon = await startContextTCS.Task;
      string contextId = msgCommon.audio_context;
      return contextId;
    }

    public async Task<string> StopContext() {
      stopContextTCS = new TaskCompletionSource<MsgCommon>();
      await wsClient.SendText($"{{\"event\": \"stop\"}}");
      MsgCommon msgCommon = await stopContextTCS.Task;
      string contextId = msgCommon.audio_context;
      return contextId;
    }

    private void OnResponse(MemoryStream inputStream)
    {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      try {
        // @TODO Find a way to deserialize only once
        var msgCommon = JSON.Parse(msgString, new MsgCommon());
        Logger.Log($"[WsClient] IN MSG {msgCommon.type}");
        switch (msgCommon.type) {
          case "started": {
            if (debug) Logger.Log($"Started message received '{msgCommon.audio_context}'");
            startContextTCS.SetResult(msgCommon);
            break;
          }
          case "stopped": {
            if (debug) Logger.Log($"Stopped message received '{msgCommon.audio_context}'");
            stopContextTCS.SetResult(msgCommon);
            break;
          }
        }
        OnMessage(msgCommon, msgString);
      } catch {
        Logger.LogError($"Error while handling message with content: {msgString}");
        throw;
      }
    }

    public async Task Shutdown() {
      await wsClient.DisconnectAsync();
    }

    public async Task SendAudio(float[] floats, int start = 0, int length = -1) {
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

      await wsClient.SendBytes(new ArraySegment<byte>(buf));
    }

  }
}