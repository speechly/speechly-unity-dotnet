using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Speechly.Tools
{
  struct SendQueueItem {
    public ArraySegment<byte> payload;
    public WebSocketMessageType type;
    public SendQueueItem(ArraySegment<byte> payload, WebSocketMessageType type) {
      this.payload = payload;
      this.type = type;
    }
  }

  public class WsClient : IDisposable
  {
    public delegate void ResponseReceivedDelegate(MemoryStream inputStream);
    public ResponseReceivedDelegate OnResponseReceived = (inputStream) => { };

    private ClientWebSocket WS;
    private CancellationTokenSource CTS;
    private BlockingCollection<SendQueueItem> SendQueue = new BlockingCollection<SendQueueItem>();
    private Task sendTask;
    private Task receiveTask;
    public int ReceiveBufferSize { get; set; } = 8192;

    public async Task ConnectAsync(string url, string authToken)
    {
      if (WS != null) {
        if (WS.State == WebSocketState.Open) return;
        else WS.Dispose();
      }
      WS = new ClientWebSocket();
      WS.Options.AddSubProtocol(authToken);
      if (CTS != null) {
        CTS.Dispose();
      }
      CTS = new CancellationTokenSource();
      await WS.ConnectAsync(new Uri(url), CTS.Token);
      receiveTask = Task.Run(ReceiveLoop, CancellationToken.None);
      sendTask = Task.Run(SendLoop, CancellationToken.None);
    }

    public async Task DisconnectAsync()
    {
      if (CTS != null) {
        CTS.Cancel();
        await Task.WhenAll(new []{sendTask, receiveTask});
        CTS.Dispose();
        CTS = null;
      }
      if (WS != null) {
        if (WS.State == WebSocketState.Open) {
          await WS.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
          await WS.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        WS.Dispose();
        WS = null;
      }
    }

    public void SendText(string text)
    {
      SendQueue.Add(new SendQueueItem(
        new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),
        WebSocketMessageType.Text        
      ));
    }

    public void SendBytes(ArraySegment<byte> byteArraySegment)
    {
      SendQueue.Add(new SendQueueItem(
        byteArraySegment,
        WebSocketMessageType.Binary        
      ));
    }

    private async Task ReceiveLoop()
    {
      var loopToken = CTS.Token;
      MemoryStream outputStream = null;
      WebSocketReceiveResult receiveResult = null;
      var buffer = new byte[ReceiveBufferSize];
      try {
        while (!loopToken.IsCancellationRequested) {
          outputStream = new MemoryStream(ReceiveBufferSize);
          do {
            receiveResult = await WS.ReceiveAsync(new ArraySegment<byte>(buffer), CTS.Token);
            if (receiveResult.MessageType != WebSocketMessageType.Close) {
              outputStream.Write(buffer, 0, receiveResult.Count);
            } else {
              Logger.Log($"Web connection closed due to {receiveResult.CloseStatus}");
              break;
            }
          } while (!loopToken.IsCancellationRequested && !receiveResult.EndOfMessage);

          if (!loopToken.IsCancellationRequested && receiveResult.MessageType != WebSocketMessageType.Close) {
            outputStream.Position = 0;
            OnResponseReceived(outputStream);
          }
          outputStream.Dispose();
        }
      }
      catch (OperationCanceledException) {
        // normal upon task/token cancellation, disregard
      } finally {
        outputStream?.Dispose();
      }
    }

    private async Task SendLoop()
    {
      while (!CTS.Token.IsCancellationRequested) {
        try {
          if(!CTS.Token.IsCancellationRequested && SendQueue.TryTake(out SendQueueItem item, -1, CTS.Token))
          {
            await WS.SendAsync(item.payload, item.type, endOfMessage: true, CTS.Token);
          }
        }
        catch (OperationCanceledException) {
          // normal upon task/token cancellation, disregard
        }
        catch {
          Logger.LogError("Error in SendLoop");
          throw;
        }
      }
    }

    public void Dispose() => DisconnectAsync().Wait();
  }
}