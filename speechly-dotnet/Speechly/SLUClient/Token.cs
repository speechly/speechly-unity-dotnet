using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Net.Http;
using System;
using System.Text;
using Speechly.Tools;

namespace Speechly.SLUClient {

  class LoginToken {
    public async Task<string> FetchToken(
      string baseUrl,
      string projectId,
      string appId,
      string deviceId
    ) {
      var httpClient = new HttpClient();
      HttpResponseMessage postResponse;

      string json;
      if (projectId != null) {
        var body = new ProjectTokenRequest{
          projectId = projectId,
          deviceId = deviceId
        };
        json = JSON.Stringify(body);
      } else if (appId != null) {
        var body = new AppTokenRequest{
          appId = appId,
          deviceId = deviceId
        };
        json = JSON.Stringify(body);
      } else {
        throw new Exception($"A Speechly appId or projectId needs to be defined to connect.");
      }

      var postRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
      postRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
      try {
        postResponse = await httpClient.SendAsync(postRequest);
        postResponse.EnsureSuccessStatusCode();
      } catch {
        if (projectId != null) {
          Logger.LogError($"Error while fetching auth token from '{baseUrl}' with Speechly projectId '{projectId}'. Is the projectId valid?");
          throw;
        } else {
          Logger.LogError($"Error while fetching auth token from '{baseUrl}' with Speechly appId '{appId}'. Is the appId valid and deployed?");
          throw;
        }
      }

      if (!(postResponse.Content is object && postResponse.Content.Headers.ContentType.MediaType == "application/json")) {
        throw new Exception("HTTP Response was invalid and cannot be deserialised.");
      }

      var contentStream = await postResponse.Content.ReadAsStreamAsync();
      TokenResponse tokenResponse;
      try {
        tokenResponse = JSON.ParseFromStream(contentStream, new TokenResponse());
      } catch (Exception) {
        throw new Exception("Invalid JSON.");
      }

      if (tokenResponse == null || tokenResponse.access_token == null) {
        throw new Exception("The response did not contain an access token.");
      }

      return tokenResponse.access_token;
    }
  }

  [DataContract]
  internal class AppTokenRequest
  {
      [DataMember]
      public string appId;
      [DataMember]
      public string deviceId;
  }

  [DataContract]
  internal class ProjectTokenRequest
  {
      [DataMember]
      public string projectId;
      [DataMember]
      public string deviceId;
  }


  [DataContract]
  internal class TokenResponse
  {
      [DataMember]
      public string access_token = null;
  }

}
