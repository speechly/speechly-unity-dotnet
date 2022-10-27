using Speechly.Types;
using Speechly.Tools;
using System.Threading.Tasks;

namespace Speechly.SLUClient {
  internal delegate void ResponseReceivedDelegate(MsgCommon msgCommon, string msgString);

  public abstract class IDecoder {
    internal abstract event ResponseReceivedDelegate OnMessage;
    internal abstract Task Initialize(AudioProcessorOptions audioProcessorOptions, ContextOptions contextOptions, AudioInfo audioInfo);
    internal abstract Task<string> Start();
    internal abstract void SendAudio(float[] floats, int start = 0, int length = -1);
    internal abstract Task<string> Stop();
    internal abstract Task Shutdown();
  }
}