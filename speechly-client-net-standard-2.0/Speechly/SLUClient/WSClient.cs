using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Speechly.SLUClient {

  public class WsClient : IDisposable
  {
    public delegate void ResponseReceivedDelegate(MemoryStream inputStream);
    public ResponseReceivedDelegate OnResponseReceived = (inputStream) => {};

    private ClientWebSocket WS;
    private CancellationTokenSource CTS;

    public int ReceiveBufferSize { get; set; } = 8192;

    public async Task ConnectAsync(string url, string authToken)
    {
      if (WS != null)
      {
        if (WS.State == WebSocketState.Open) return;
        else WS.Dispose();
      }
      WS = new ClientWebSocket();
      WS.Options.AddSubProtocol(authToken);
      if (CTS != null) CTS.Dispose();
      CTS = new CancellationTokenSource();
      await WS.ConnectAsync(new Uri(url), CTS.Token);
      await Task.Factory.StartNew(ReceiveLoop, CTS.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task DisconnectAsync()
    {
      if (WS is null) return;
      // @TODO: requests cleanup code, sub-protocol dependent.
      if (WS.State == WebSocketState.Open)
      {
        CTS.CancelAfter(TimeSpan.FromSeconds(2));
        await WS.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
        await WS.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
      }
      WS.Dispose();
      WS = null;
      CTS.Dispose();
      CTS = null;
    }

    public async Task SendText(string text) {
      // Logger.Log($"[WsClient] OUT {text}");
      await WS.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, CTS.Token);
    }

    public async Task SendBytes(ArraySegment<byte> byteArraySegment) {
      // Logger.Log($"[WsClient] OUT BYTES");
      await WS.SendAsync(byteArraySegment, WebSocketMessageType.Binary, true, CTS.Token);
    }

    private async Task ReceiveLoop()
    {
      var loopToken = CTS.Token;
      MemoryStream outputStream = null;
      WebSocketReceiveResult receiveResult = null;
      var buffer = new byte[ReceiveBufferSize];
      try
      {
        while (!loopToken.IsCancellationRequested)
        {
          outputStream = new MemoryStream(ReceiveBufferSize);
          do
          {
            receiveResult = await WS.ReceiveAsync(new ArraySegment<byte>(buffer), CTS.Token);
            if (receiveResult.MessageType != WebSocketMessageType.Close)
              outputStream.Write(buffer, 0, receiveResult.Count);
          }
          while (!receiveResult.EndOfMessage);
          if (receiveResult.MessageType == WebSocketMessageType.Close) break;
          outputStream.Position = 0;
          OnResponseReceived(outputStream);
          outputStream.Dispose();
        }
      }
      catch (TaskCanceledException) { }
      finally
      {
        outputStream?.Dispose();
      }
    }

    public void Dispose() => DisconnectAsync().Wait();
  }
}