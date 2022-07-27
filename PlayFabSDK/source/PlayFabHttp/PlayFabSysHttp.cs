using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AltV.Net.Client;
using AltV.Net.Client.Elements.Data;
using AltV.Net.Client.Elements.Interfaces;

namespace PlayFab.Internal
{
    public class PlayFabSysHttp : ITransportPlugin
    {
        private readonly IHttpClient _client = Alt.CreateHttpClient();


        public async Task<object> DoPost(string fullUrl, object request, Dictionary<string, string> extraHeaders)
        {
            await new PlayFabUtil.SynchronizationContextRemover();

            var serializer = PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer);
            string bodyString;

            if (request == null)
            {
                bodyString = "{}";
            }
            else
            {
                bodyString = serializer.SerializeObject(request);
            }

            HttpResponse httpResponse;
            string httpResponseString;
            string requestId;
            bool hasReqId = false;


            _client.SetExtraHeader("Content-Type", "application/json");
            _client.SetExtraHeader("X-PlayFabSDK", PlayFabSettings.SdkVersionString);

            if (extraHeaders != null)
            {
                foreach (var headerPair in extraHeaders)
                {
                    // Special case for Authorization header
                    if (headerPair.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        _client.SetExtraHeader("Authorization", $"Bearer {headerPair.Value}");
                    }
                    else
                    {
                        _client.SetExtraHeader(headerPair.Key, headerPair.Value);
                    }
                }
            }

            try
            {
                httpResponse =
                    await _client.Post(fullUrl, bodyString);
               
                httpResponseString = httpResponse.Body.ToString();
                hasReqId = httpResponse.Headers.TryGetValue("X-RequestId", out requestId);
            }
            catch (Exception e)
            {
                return new PlayFabError
                {
                    Error = PlayFabErrorCode.ConnectionError,
                    ErrorMessage = e.Message
                };
            }

            if (!(httpResponse.StatusCode >= 200 && httpResponse.StatusCode <= 299))
            {
                var error = new PlayFabError();

                if (string.IsNullOrEmpty(httpResponseString))
                {
                    error.HttpCode = (int)httpResponse.StatusCode;
                    error.HttpStatus = httpResponse.StatusCode.ToString();
                    error.RequestId = GetRequestId(hasReqId, requestId);
                    return error;
                }

                PlayFabJsonError errorResult;
                try
                {
                    errorResult = serializer.DeserializeObject<PlayFabJsonError>(httpResponseString);
                }
                catch (Exception e)
                {
                    error.HttpCode = (int)httpResponse.StatusCode;
                    error.HttpStatus = httpResponse.StatusCode.ToString();
                    error.Error = PlayFabErrorCode.JsonParseError;
                    error.ErrorMessage = e.Message;
                    error.RequestId = GetRequestId(hasReqId, requestId);
                    ;
                    return error;
                }

                error.HttpCode = errorResult.code;
                error.HttpStatus = errorResult.status;
                error.Error = (PlayFabErrorCode)errorResult.errorCode;
                error.ErrorMessage = errorResult.errorMessage;
                error.RetryAfterSeconds = errorResult.retryAfterSeconds;

                if (errorResult.errorDetails != null)
                {
                    error.ErrorDetails = new Dictionary<string, string[]>();
                    foreach (var detail in errorResult.errorDetails)
                    {
                        error.ErrorDetails.Add(detail.Key, detail.Value);
                    }
                }

                error.RequestId = GetRequestId(hasReqId, requestId);
                ;

                return error;
            }

            if (string.IsNullOrEmpty(httpResponseString))
            {
                return new PlayFabError
                {
                    Error = PlayFabErrorCode.Unknown,
                    ErrorMessage = "Internal server error",
                    RequestId = GetRequestId(hasReqId, requestId)
                };
            }

            return httpResponseString;
        }

        private string GetRequestId(bool hasReqId, string reqIdContainer)
        {
            const string defaultReqId = "NoRequestIdFound";
            string reqId = "";

            try
            {
                reqId = hasReqId ? reqIdContainer : defaultReqId;
            }
            catch (Exception e)
            {
                return "Failed to Enumerate RequestId. Exception message: " + e.Message;
            }

            return reqId;
        }
    }
}