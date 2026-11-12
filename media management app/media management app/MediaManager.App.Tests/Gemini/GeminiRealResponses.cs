namespace MediaManager.App.Tests.Gemini;

/// <summary>
/// Response bodies captured from the real Gemini API on 12 Nov 2026 (probe in the parser-audit notes).
/// No API key appears in any of them.
/// </summary>
internal static class GeminiRealResponses
{
    /// <summary>From 06-2.5-flash-lite-app-shape.json.</summary>
    public const string DailyQuotaExceeded =
        """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota, please check your plan and billing details. For more information on this error, head to: https://aistudio.google.com/docs/rate-limits. To monitor your current usage, head to: https://ai.dev/rate-limit. \n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 20, model: gemini-2.5-flash-lite, description: Generate content requests per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.\nPlease retry in 18h4m25.456059917s.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.Help",
                "links": [
                  {
                    "description": "Learn more about Gemini API quotas",
                    "url": "https://aistudio.google.com/docs/rate-limits"
                  }
                ]
              },
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "description": "Generate content requests per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.",
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
                    "quotaId": "GenerateRequestsPerDayPerProjectPerModel-FreeTier",
                    "quotaDimensions": {
                      "model": "gemini-2.5-flash-lite",
                      "location": "global"
                    },
                    "quotaValue": "20"
                  }
                ]
              },
              {
                "@type": "type.googleapis.com/google.rpc.RetryInfo",
                "retryDelay": "65065s"
              }
            ]
          }
        }
        """;

    /// <summary>From 01-limit0-3.1-pro-preview-429.json.</summary>
    public const string NoFreeTier =
        """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota, please check your plan and billing details. For more information on this error, head to: https://aistudio.google.com/docs/rate-limits. To monitor your current usage, head to: https://ai.dev/rate-limit. \n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 0, model: gemini-3.1-pro, description: Generate content requests per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.\n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 0, model: gemini-3.1-pro, description: Generate content requests per minute limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.\n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_input_token_count, limit: 0, model: gemini-3.1-pro, description: Generate content input tokens per minute limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.\n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_input_token_count, limit: 0, model: gemini-3.1-pro, description: Generate content input tokens per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.\nPlease retry in 18h6m18.044561453s.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.Help",
                "links": [
                  {
                    "description": "Learn more about Gemini API quotas",
                    "url": "https://aistudio.google.com/docs/rate-limits"
                  }
                ]
              },
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "description": "Generate content requests per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.",
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
                    "quotaId": "GenerateRequestsPerDayPerProjectPerModel-FreeTier",
                    "quotaDimensions": {
                      "model": "gemini-3.1-pro",
                      "location": "global"
                    }
                  },
                  {
                    "description": "Generate content requests per minute limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.",
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
                    "quotaId": "GenerateRequestsPerMinutePerProjectPerModel-FreeTier",
                    "quotaDimensions": {
                      "model": "gemini-3.1-pro",
                      "location": "global"
                    }
                  },
                  {
                    "description": "Generate content input tokens per minute limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.",
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_input_token_count",
                    "quotaId": "GenerateContentInputTokensPerModelPerMinute-FreeTier",
                    "quotaDimensions": {
                      "location": "global",
                      "model": "gemini-3.1-pro"
                    }
                  },
                  {
                    "description": "Generate content input tokens per day limit exceeded on Free Tier. See https://aistudio.google.com/docs/rate-limits.",
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_input_token_count",
                    "quotaId": "GenerateContentInputTokensPerModelPerDay-FreeTier",
                    "quotaDimensions": {
                      "model": "gemini-3.1-pro",
                      "location": "global"
                    }
                  }
                ]
              },
              {
                "@type": "type.googleapis.com/google.rpc.RetryInfo",
                "retryDelay": "65178s"
              }
            ]
          }
        }
        """;

    /// <summary>From 00-invalid-key-400.json.</summary>
    public const string InvalidApiKey =
        """
        {
          "error": {
            "code": 400,
            "message": "API key not valid. Please pass a valid API key.",
            "status": "INVALID_ARGUMENT",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                "reason": "API_KEY_INVALID",
                "domain": "googleapis.com",
                "metadata": {
                  "service": "generativelanguage.googleapis.com"
                }
              },
              {
                "@type": "type.googleapis.com/google.rpc.LocalizedMessage",
                "locale": "en-US",
                "message": "API key not valid. Please pass a valid API key."
              }
            ]
          }
        }
        """;

    /// <summary>From 02-3.5-flash-lite-minimal.json.</summary>
    public const string HighDemand =
        """
        {
          "error": {
            "code": 503,
            "message": "This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later.",
            "status": "UNAVAILABLE"
          }
        }
        """;

    /// <summary>From 04-3.6-flash-minimal.json.</summary>
    public const string Success36Flash =
        """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "{\"m\":[[0,1],[1,2]],\"w\":[]}",
                    "thoughtSignature": "EmkKZwFpFH0Tbj8wwPsA9tiiecxW3xG7ozz80x/MalNEV7rkX6/QU49TvbWRBIjLgHvdmVyeOFpKJW7m8QR4HTWFEdRyCxCM2GtntVVG26UyN0RWGwtDagJrVh+dXPqFDNWxEtG4vC4ACls="
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP",
              "index": 0
            }
          ],
          "usageMetadata": {
            "promptTokenCount": 114,
            "candidatesTokenCount": 16,
            "totalTokenCount": 130,
            "promptTokensDetails": [
              {
                "modality": "TEXT",
                "tokenCount": 114
              }
            ],
            "serviceTier": "standard"
          },
          "modelVersion": "gemini-3.6-flash",
          "responseId": "McX1arW2NIbMrfcPpeDAmA8"
        }
        """;
}
