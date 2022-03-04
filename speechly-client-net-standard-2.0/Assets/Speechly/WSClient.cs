using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

public class WsClient : IDisposable
{
  public delegate void ResponseReceivedDelegate(MemoryStream inputStream);
  public ResponseReceivedDelegate onResponseReceived = (inputStream) => {};

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

  public async Task sendText(string text) {
    await WS.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, CTS.Token);
  }

  public async Task sendBytes(ArraySegment<byte> byteArraySegment) {
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
        onResponseReceived(outputStream);
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
