using System.Runtime.Serialization.Json;
using System.Text;
using System.IO;

namespace Speechly.Tools {
  public class JSON {
    public static MemoryStream StringifyToStream<T>(T obj) {  
      var stream = new MemoryStream();
      var serializer = new DataContractJsonSerializer(typeof(T));
      serializer.WriteObject(stream, obj);
      return stream;
    }

    public static string Stringify<T>(T obj) {  
      var stream = StringifyToStream(obj);
      byte[] json = stream.ToArray();
      stream.Close();
      return Encoding.UTF8.GetString(json, 0, json.Length);
    }

    public static T ParseFromStream<T>(Stream stream, T obj) where T: class {
      var serializer = new DataContractJsonSerializer(obj.GetType());
      obj = serializer.ReadObject(stream) as T;
      stream.Close();
      return obj;
    }

    public static T Parse<T>(string json, T obj) where T: class {
      var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
      return ParseFromStream(stream, obj);
    }
  }
}