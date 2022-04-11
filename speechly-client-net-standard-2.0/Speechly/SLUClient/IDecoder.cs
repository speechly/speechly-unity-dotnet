using System.Threading.Tasks;

namespace Speechly.SLUClient {
  public delegate void ResponseReceivedDelegate(MsgCommon msgCommon, string msgString);

  public interface IDecoder {
    event ResponseReceivedDelegate OnMessage;
    Task Initialize();
    Task<string> StartContext();
    void SendAudio(float[] floats, int start = 0, int length = -1);
    Task<string> StopContext();
    Task Shutdown();
  }
}