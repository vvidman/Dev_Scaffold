/*

   Copyright 2026 Viktor Vidman

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

 */

namespace Scaffold.Application;

/// <summary>
/// Fail-fast exception: input validation failed.
/// Thrown when a path reference does not exist or cannot be read.
/// </summary>
public class ScaffoldInputValidationException : Exception
{
    public string StepId { get; }
    public string FieldName { get; }
    public string InvalidPath { get; }

    public ScaffoldInputValidationException(
        string stepId,
        string fieldName,
        string invalidPath)
        : base(BuildMessage(stepId, fieldName, invalidPath))
    {
        StepId = stepId;
        FieldName = fieldName;
        InvalidPath = invalidPath;
    }

    private static string BuildMessage(string stepId, string fieldName, string path) =>
        $"""
        [SCAFFOLD ERROR] Input validation failed.
        Step: {stepId}
        Field: {fieldName}
        Path: {path}
        Reason: File not found.

        The run has stopped. Fix the input schema and run again.
        """;
}
