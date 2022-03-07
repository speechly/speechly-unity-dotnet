using System.Runtime.Serialization.Json;
using System.Text;
using System.IO;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Speechly.SLUClient {
  public class JSON {
    public static MemoryStream JSONSerializeStream<T>(T obj) {  
      // Create a stream to serialize the object to.
      var stream = new MemoryStream();
      // Serializer the the object to the stream
      var serializer = new DataContractJsonSerializer(typeof(T));
      serializer.WriteObject(stream, obj);
      return stream;
    }

    public static string JSONSerialize<T>(T obj) {  
      // Create a stream to serialize the object to.
      var stream = new MemoryStream();
      // Serializer the the object to the stream
      var serializer = new DataContractJsonSerializer(typeof(T));
      serializer.WriteObject(stream, obj);
      byte[] json = stream.ToArray();
      stream.Close();
      return Encoding.UTF8.GetString(json, 0, json.Length);
  /*
      MemoryStream stream = new MemoryStream();
      DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));  
      serializer.WriteObject(stream, obj);  
      stream.Position = 0;  
      StreamReader sr = new StreamReader(stream);
      string jsonString = sr.ReadToEnd();
      stream.Close();
      return jsonString;
  */
    }

    public static T JSONDeserialize<T>(string json, T obj) where T: class {
      var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
      var serializer = new DataContractJsonSerializer(obj.GetType());
      obj = serializer.ReadObject(stream) as T;
      stream.Close();
      return obj;
    }

    public static T JSONDeserializeStream<T>(Stream stream, T obj) where T: class {
      var serializer = new DataContractJsonSerializer(obj.GetType());
      obj = serializer.ReadObject(stream) as T;
      stream.Close();
      return obj;
    }
  }
}