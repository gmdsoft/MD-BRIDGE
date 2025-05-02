using CSharpFunctionalExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using MD.BRIDGE.Utils;
using LogModule;

namespace MD.BRIDGE.Services

{
    public static class LogExctractorService
    {
        private static readonly Regex _regex = new Regex(@"\[(.*?)\]");

        public static IEnumerable<string> Extract(string filePath, DateTimeOffset start, DateTimeOffset end)
        {
            if (!File.Exists(filePath))
                return new List<string>();

            List<string> logs = new List<string>();

            using (var reader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line.IsNullOrEmpty())
                        continue;

                    try
                    {
                        ExtractTimeStamp(line)
                            .Bind(timestamp => timestamp.IsBetween(start, end)
                                ? Result.Success(timestamp)
                                : Result.Failure<DateTimeOffset>("Timestamp is not in target period."))
                            .Bind(_ => ExtractLogString(line))
                            .Tap(logs.Add);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"Fail to extract log string from {filePath}.\n{e.Message}");
                    }
                }
            }

            return logs;
        }

        private static Result<DateTimeOffset> ExtractTimeStamp(string record)
        {
            var match = _regex.Match(record);
            if (!match.Success)
                return Result.Failure<DateTimeOffset>("Fail to extract timestamp.");

            if (DateTimeOffset.TryParse(match.Groups[1].Value, out var timestamp))
            {
                return timestamp;
            }
            else
            {
                return Result.Failure<DateTimeOffset>("Fail to parse timestamp.");
            }
        }

        private static Result<string> ExtractLogString(string record)
        {
            var match = _regex.Match(record);
            if (!match.Success)
                return Result.Failure<string>("Fail to extract log string.");

            return record.Replace(match.Value, "").Trim();
        }
    }
}