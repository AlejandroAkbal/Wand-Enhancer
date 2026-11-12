using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WandEnhancer.Core.Js;
using WandEnhancer.Models;

namespace WandEnhancer.Core
{
    public static class EnhancerConfig
    {
        /// <summary>
        /// One patch site: a regular expression anchored on identifiers that survive minification,
        /// the embedded snippet that gets spliced in (<c>Patches/*.js</c>), and how the splice is placed.
        /// </summary>
        public sealed class PatchEntry
        {
            public string Name { get; set; }
            public Regex Target { get; set; }

            /// <summary>Resource name under <c>patches/</c>, without the extension.</summary>
            public string Payload { get; set; }

            /// <summary>Group whose span is replaced. Defaults to the whole match ("0").</summary>
            public string SpanGroup { get; set; }

            /// <summary>Insert the payload after the whole match instead of replacing a group.</summary>
            public bool InsertAfterMatch { get; set; }

            /// <summary>Insert the payload before the whole match, keeping the matched text intact.</summary>
            public bool InsertBeforeMatch { get; set; }

            /// <summary>Insert the payload after the span captured by <see cref="SpanGroup"/>.</summary>
            public bool InsertAfterGroup { get; set; }

            /// <summary>Insert the payload just before the closing brace that ends the match.</summary>
            public bool InsertBeforeMatchEnd { get; set; }

            public string[] CandidateFileNames { get; set; }
            public string[] SearchHints { get; set; }
            public string[] CapabilityHints { get; set; }
            public bool CapabilityDetected { get; set; }
            public bool Applied { get; set; }
            public bool IsResolved { get; set; }

            /// <summary>A patch whose absence on a build does not fail the run.</summary>
            public bool IsOptional { get; set; }

            /// <summary>Placeholders that live outside the matched span, resolved against the whole file.</summary>
            public Func<string, string[]> FilePlaceholders { get; set; }

            public JsEdit[] Locate(JsCursor cursor)
            {
                IsResolved = true;
                var match = Target.Match(cursor.Text);
                if (!match.Success || string.IsNullOrEmpty(Payload))
                {
                    return Array.Empty<JsEdit>();
                }

                var arguments = new List<string>();
                foreach (var group in Target.GetGroupNames())
                {
                    if (int.TryParse(group, out _))
                    {
                        continue;
                    }

                    arguments.Add(group);
                    arguments.Add(match.Groups[group].Value);
                }

                if (FilePlaceholders != null)
                {
                    arguments.AddRange(FilePlaceholders(cursor.Text));
                }

                string text = PatchPayload.Load(Payload, arguments.ToArray());
                if (string.IsNullOrEmpty(text))
                {
                    return Array.Empty<JsEdit>();
                }

                if (InsertAfterGroup)
                {
                    var anchor = match.Groups[string.IsNullOrEmpty(SpanGroup) ? "0" : SpanGroup];
                    return new[] { new JsEdit(anchor.Index + anchor.Length, text) };
                }

                if (InsertBeforeMatch)
                {
                    return new[] { new JsEdit(match.Index, text) };
                }

                if (InsertBeforeMatchEnd)
                {
                    return new[] { new JsEdit(match.Index + match.Length - 1, text) };
                }

                if (InsertAfterMatch)
                {
                    return new[] { new JsEdit(match.Index + match.Length, text) };
                }

                string spanName = string.IsNullOrEmpty(SpanGroup) ? "0" : SpanGroup;
                var span = match.Groups[spanName];
                return new[] { new JsEdit(span.Index, span.Index + span.Length, text) };
            }
        }

        private static string Pick(string source, string pattern, string group, string patchName)
        {
            var match = Regex.Match(source, pattern, RegexOptions.Singleline);
            if (!match.Success || !match.Groups[group].Success || string.IsNullOrEmpty(match.Groups[group].Value))
            {
                throw new Exception($"{patchName} failed to resolve {group}");
            }

            return match.Groups[group].Value;
        }

        private static string[] TrainerAndMetadata(string source, string patchName)
        {
            var match = Regex.Match(
                source,
                @"this\s*\.\s*(?<trainer>#[\w$]+)\s*\?\.\s*getMetadata\(\s*(?<metadata>[\w$]+\s*\.\s*[\w$]+)\s*\)",
                RegexOptions.Singleline);
            if (!match.Success)
            {
                throw new Exception($"{patchName} failed to resolve the trainer metadata accessor");
            }

            return new[]
            {
                "trainer", match.Groups["trainer"].Value,
                "metadata", match.Groups["metadata"].Value
            };
        }

        private static string[] TrainerRemoteSource(string source, string patchName)
        {
            var values = new List<string>(TrainerAndMetadata(source, patchName));
            values.Add("remoteSource");
            values.Add(Pick(source, @"(?<remoteSource>[\w$]+(?:\.[\w$]+)+)\.Remote\b", "remoteSource", patchName));
            return values.ToArray();
        }

        private static string[] TrainerId(string source, string patchName)
        {
            return new[]
            {
                "trainerId",
                "this." + Pick(source, @"trainerId\s*:\s*this\s*\.\s*(?<trainerId>#[\w$]+)", "trainerId", patchName)
            };
        }

        public static Dictionary<EPatchType, PatchEntry[]> GetInstance()
        {
            return new Dictionary<EPatchType, PatchEntry[]>
            {
                {
                    EPatchType.ActivatePro,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "disableNativeRemotePairing",
                            SearchHints = new[] { "requestRemoteAuthCode", "/v3/auth/remote_code" },
                            CapabilityHints = new[] { "requestRemoteAuthCode" },
                            Target = new Regex(@"requestRemoteAuthCode\(\)\s*\{(?<body>[^{}]*)\}",
                                RegexOptions.Singleline),
                            Payload = "disable-native-pairing",
                            SpanGroup = "body"
                        }
                    }
                },
                {
                    EPatchType.DisableUpdates,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "checkForUpdate",
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "ACTION_CHECK_FOR_UPDATE" },
                            CapabilityHints = new[] { "ACTION_CHECK_FOR_UPDATE" },
                            Target = new Regex(@"registerHandler\(\s*(?<patch>""ACTION_CHECK_FOR_UPDATE""\s*,[\s\S]*?)\)\s*;",
                                RegexOptions.Singleline),
                            Payload = "disable-updates",
                            SpanGroup = "patch"
                        }
                    }
                },
                {
                    EPatchType.DevToolsOnF12,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "devToolsBeforeInputEvent",
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "whenReady().then(" },
                            CapabilityHints = new[] { "whenReady().then(" },
                            Target = new Regex(@"(?<app>[\w$]+)\s*\.\s*whenReady\(\)\s*\.\s*then\("),
                            Payload = "devtools-f12",
                            InsertBeforeMatch = true
                        }
                    }
                },
                {
                    EPatchType.RemoteWebPanelPreview,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "remoteBridgeMainBoot",
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "whenReady().then(run)" },
                            CapabilityHints = new[] { "whenReady().then(run)" },
                            Target = new Regex(@"(?<app>[\w$]+)\s*\.\s*whenReady\(\)\s*\.\s*then\(\s*run\s*\)"),
                            Payload = "remote-bridge-boot",
                            SpanGroup = "0"
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeBindHandler",
                            SearchHints = new[] { "setCurrentTrainer" },
                            CapabilityHints = new[] { "setCurrentTrainer" },
                            Target = new Regex(
                                @"(?<head>setCurrentTrainer\(\s*[\w$]+\s*,\s*[\w$]+\s*=\s*null\s*\)\s*\{\s*const\s+(?<trainerInfo>[\w$]+)\s*=\s*[\w$]+\s*\?\.\s*trainerId\s*\|\|\s*null[\s\S]*?;)",
                                RegexOptions.Singleline),
                            Payload = "remote-bridge-renderer",
                            SpanGroup = "head",
                            InsertAfterGroup = true,
                            FilePlaceholders = source => TrainerRemoteSource(source, "remoteBridgeBindHandler")
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeReset",
                            SearchHints = new[] { "Date.now().toString()" },
                            CapabilityHints = new[] { "Date.now().toString()" },
                            Target = new Regex(
                                @"(?<method>#[\w$]+)\(\)\s*\{(?<body>[^{}]*?Date\.now\(\)\.toString\(\)[^{}]*)\}",
                                RegexOptions.Singleline),
                            Payload = "remote-bridge-reset",
                            InsertBeforeMatchEnd = true
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeSyncSnapshot",
                            SearchHints = new[] { "client-state" },
                            CapabilityHints = new[] { "client-state" },
                            Target = new Regex(
                                @"this\s*\.\s*(?<channel>#[\w$]+)\s*\?\.\s*send\(\s*""client-state""\s*,\s*\{(?<snapshot>[^{}]*)\}\s*\)",
                                RegexOptions.Singleline),
                            Payload = "remote-bridge-sync",
                            InsertAfterMatch = true,
                            FilePlaceholders = source => TrainerAndMetadata(source, "remoteBridgeSyncSnapshot")
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeValueSubscription",
                            SearchHints = new[] { "onValueSet" },
                            CapabilityHints = new[] { "onValueSet" },
                            Target = new Regex(@"(?<head>#[\w$]+\(\s*[\w$]+\s*,\s*[\w$]+\s*\)\s*\{)(?=\s*[\w$]+\s*\.\s*push\(\s*[\w$]+\s*\.\s*onValueSet\()"),
                            Payload = "remote-bridge-value-subscription",
                            InsertAfterMatch = true,
                            FilePlaceholders = source => TrainerId(source, "remoteBridgeValueSubscription")
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeValueDelta",
                            SearchHints = new[] { "client-value-changed" },
                            CapabilityHints = new[] { "client-value-changed" },
                            Target = new Regex(@"(?<head>onValueSet\(\s*\(?\s*(?<change>[\w$]+)\s*\)?\s*=>\s*\{)(?=[^}]*client-value-changed)"),
                            Payload = "remote-bridge-value-delta",
                            InsertAfterMatch = true
                        }
                    }
                }
            };
        }
    }
}
