using Speechly.Types;
using System.Threading.Tasks;

namespace Speechly.SLUClient {
  internal delegate void ResponseReceivedDelegate(MsgCommon msgCommon, string msgString);

  public abstract class IDecoder {
    internal abstract event ResponseReceivedDelegate OnMessage;
    internal abstract Task Initialize();
    internal abstract Task<string> StartContext();
    internal abstract void SendAudio(float[] floats, int start = 0, int length = -1);
    internal abstract Task<string> StopContext();
    internal abstract Task Shutdown();
  }
}