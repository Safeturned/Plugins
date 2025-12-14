using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using Safeturned.Module.RateLimiting;
using UnityEngine;
using UnityEngine.Networking;

namespace Safeturned.Module.Uploader;

public class UploadClient
{
    private readonly string _apiBaseUrl;
    private readonly string _apiKey;
    private readonly RateLimitBucket _bucket;

    public UploadClient(string apiBaseUrl, string apiKey, RateLimitBucket bucket)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _bucket = bucket;
    }

    public IEnumerator UploadFile(string filePath, string fileName, bool forceAnalyze, Action<bool> onComplete)
    {
        if (!_bucket.TryConsume(out _))
        {
            ModuleLogger.Info("Upload skipped due to empty bucket.");
            onComplete(false);
            yield break;
        }

        var url = $"{_apiBaseUrl}/v1.0/files?forceAnalyze={(forceAnalyze ? "true" : "false")}";

        var attempts = 0;
        const int maxAttempts = 3;
        var backoffSeconds = 1f;

        while (attempts < maxAttempts)
        {
            attempts++;

            byte[] payload;
            try
            {
                payload = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                ModuleLogger.Error("Failed to read file {0}: {1}", filePath, ex.Message);
                onComplete(false);
                yield break;
            }

            var form = new WWWForm();
            form.AddBinaryData("file", payload, fileName, "application/octet-stream");

            using var request = UnityWebRequest.Post(url, form);
            request.SetRequestHeader("X-API-Key", _apiKey);
            request.SetRequestHeader("X-Client", "safeturned-module");

            yield return request.SendWebRequest();

            UpdateRateLimit(request);

            if (request.result == UnityWebRequest.Result.Success)
            {
                ModuleLogger.Info("Upload succeeded for {0}", fileName);
                onComplete(true);
                yield break;
            }

            var errorDetails = TryParseErrorResponse(request);
            if (!string.IsNullOrEmpty(errorDetails))
            {
                ModuleLogger.Error("Upload failed for {0}: {1} ({2}) - {3} (attempt {4}/{5})",
                    fileName, request.error, request.responseCode, errorDetails, attempts, maxAttempts);
            }
            else
            {
                ModuleLogger.Error("Upload failed for {0}: {1} ({2}) attempt {3}/{4}",
                    fileName, request.error, request.responseCode, attempts, maxAttempts);
            }

            if (request.responseCode == 429)
            {
                var retryAfterHeader = request.GetResponseHeader("Retry-After");
                if (int.TryParse(retryAfterHeader, out var retryAfter) && retryAfter > 0)
                {
                    ModuleLogger.Info("Rate limited. Retrying after {0}s", retryAfter);
                    yield return new WaitForSeconds(retryAfter);
                    continue;
                }
            }

            if (attempts < maxAttempts)
            {
                yield return new WaitForSeconds(backoffSeconds);
                backoffSeconds *= 2;
            }
        }
        onComplete(false);
    }

    private void UpdateRateLimit(UnityWebRequest request)
    {
        var limitHeader = request.GetResponseHeader("X-RateLimit-Limit");
        var remainingHeader = request.GetResponseHeader("X-RateLimit-Remaining");
        var resetHeader = request.GetResponseHeader("X-RateLimit-Reset");

        // Debug: Log raw headers to diagnose rate limit issues
        if (string.IsNullOrEmpty(limitHeader) || string.IsNullOrEmpty(remainingHeader) || string.IsNullOrEmpty(resetHeader))
        {
            ModuleLogger.Info("Rate limit headers missing - Limit: '{0}', Remaining: '{1}', Reset: '{2}'",
                limitHeader ?? "(null)", remainingHeader ?? "(null)", resetHeader ?? "(null)");
            return;
        }

        if (int.TryParse(limitHeader, out var limit) && int.TryParse(remainingHeader, out var remaining) && long.TryParse(resetHeader, out var reset))
        {
            _bucket.SeedFromHeaders(limit, remaining, reset);
            ModuleLogger.Info("Rate limit updated: {0}/{1} (resets at {2})", remaining, limit, reset);
        }
        else
        {
            ModuleLogger.Info("Rate limit headers could not be parsed - Limit: '{0}', Remaining: '{1}', Reset: '{2}'",
                limitHeader, remainingHeader, resetHeader);
        }
    }

    private string TryParseErrorResponse(UnityWebRequest request)
    {
        try
        {
            var responseText = request.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            var json = JObject.Parse(responseText);
            var error = json["error"]?.ToString();
            var message = json["message"]?.ToString();

            if (!string.IsNullOrEmpty(error))
            {
                return error;
            }
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
