﻿using System.Threading.Tasks;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace Speechly.SLUClient {
  public class AppTokenRequest
  {
      public string appId;
      public string deviceId;
  }

  public class ProjectTokenRequest
  {
      public string projectId;
      public string deviceId;
  }


  public class TokenResponse
  {
      public string access_token;
  }

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
      } else {
        var body = new AppTokenRequest{
          appId = appId,
          deviceId = deviceId
        };
        json = JSON.Stringify(body);
      }
      var postRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
      postRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
      try {
        postResponse = await httpClient.SendAsync(postRequest);
      } catch ( Exception ) {
        throw new Exception($"Problem fetching auth token from '{baseUrl}'." );
      }

      postResponse.EnsureSuccessStatusCode();

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
}