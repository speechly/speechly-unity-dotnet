using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Text;
using Speechly.Types;
using Speechly.Tools;

namespace Speechly.SLUClient {
/// <summary>
/// Provides speech processing with Speechly's cloud SLU service.
///
/// Internally handles authentication using https and audio streaming via secure websocket.
/// Audio is streamed when listening is started with SpeechlyClient.Start().
/// Streaming stops upon call to SpeechlyClient.Stop().
/// </summary>

  public class CloudDecoder: IDecoder {
    override internal event ResponseReceivedDelegate OnMessage = (MsgCommon msgCommon, string msgString) => {};
    private string loginUrl = null;
    private string apiUrl = null;
    private string projectId = null;
    private string appId = null;
    private string deviceId = null;
    private bool debug = false;

    private WsClient wsClient = null;
    private ConcurrentQueue<TaskCompletionSource<string>> startContextTCS = new ConcurrentQueue<TaskCompletionSource<string>>();
    private ConcurrentQueue<TaskCompletionSource<string>> stopContextTCS = new ConcurrentQueue<TaskCompletionSource<string>>();
    private StartMessage startMessage;

/// <summary>
/// Initialize speech processing with Speechly's cloud SLU service.
/// </summary>
/// <param name="deviceId">An unique string id for the device.</param>
/// <param name="apiUrl">Speechly SLU API url (default: `https://api.speechly.com`)</param>
/// <param name="projectId">Speechly app id for authentication. Only provide either app id or project id, not both (default: `null`).</param>
/// <param name="appId">Speechly project id for authentication. Only provide either app id or project id, not both (default: `null`).</param>
/// <param name="debug">Enable debug prints thru <see cref="Logger.Log"/> delegate. (default: `false`)</param>

    public CloudDecoder(
      string deviceId,
      string apiUrl = null,
      string appId = null,
      string projectId = null,
      bool debug = false
    ) {
      this.deviceId = deviceId;
      this.debug = debug;

      if (apiUrl == null) {
        apiUrl = "https://api.speechly.com";
      }

      this.loginUrl = $"{apiUrl}/login";
      this.apiUrl = $"{apiUrl.Replace("http", "ws")}/ws/v1?sampleRate=16000";

      if (String.IsNullOrWhiteSpace(projectId) && String.IsNullOrWhiteSpace(appId)) {
        throw new Exception($"Either appId or projectId has to be provided. Get it from {apiUrl}/dashboard");
      }
      if (!String.IsNullOrWhiteSpace(projectId) && !String.IsNullOrWhiteSpace(appId)) {
        throw new Exception($"Please log in with either projectId or appId, not both. With projectId login you may use all appIds within the project. Get it from {apiUrl}/dashboard");
      }

      this.appId = appId;
      this.projectId = projectId;
    }

    override internal async Task Initialize(AudioProcessorOptions audioProcessorOptions, ContextOptions contextOptions, AudioInfo _) {
      if (debug) Logger.Log("Initializing and connecting Cloud SLU...");
      var tokenFetcher = new LoginToken();
      string token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

      if (debug) Logger.Log($"token: {token}");

      wsClient = new WsClient();
      wsClient.OnResponseReceived = OnResponse;

      // Prebuild the start message
      startMessage = new StartMessage() {eventType = "start", appId = this.appId};
      startMessage.options = new StartMessage.Options();
      startMessage.options.silence_triggered_segmentation = new[] {Convert.ToString(contextOptions.SilenceSegmentationMillis)};
      if (contextOptions.BoostVocabulary != null && !string.IsNullOrEmpty(contextOptions.BoostVocabulary.Vocabulary)) {
        startMessage.options.vocabulary = contextOptions.BoostVocabulary.Vocabulary.ToUpper().Split('\n');
        startMessage.options.vocabulary_bias = new[] {Convert.ToString(contextOptions.BoostVocabulary.Weight)};
      }

      await wsClient.ConnectAsync(apiUrl, token);
      if (debug) Logger.Log("Cloud SLU ready");
    }

    override internal Task<string> Start() {
      var tcs = new TaskCompletionSource<string>();
      startContextTCS.Enqueue(tcs);
      
      wsClient.SendText(JSON.Stringify<StartMessage>(startMessage));

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

    override internal void SendAudio(float[] floats, int start = 0, int length = -1) {
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

    override internal Task<string> Stop() {
      var tcs = new TaskCompletionSource<string>();
      stopContextTCS.Enqueue(tcs);
      wsClient.SendText($"{{\"event\": \"stop\"}}");
      return tcs.Task;
    }

    override internal async Task Shutdown() {
      if (debug) Logger.Log("Cloud SLU shutting down...");
      await wsClient.DisconnectAsync();
      if (debug) Logger.Log("Shutdown completed.");
    }

  }
}