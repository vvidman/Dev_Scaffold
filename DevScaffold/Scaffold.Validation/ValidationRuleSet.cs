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

namespace Scaffold.Validation;

/// <summary>
/// Deklaratív validációs szabálykészlet, yaml fájlból töltve.
/// A kódban lévő per-step logikát egészíti ki – nem helyettesíti.
/// </summary>
public class ValidatorRuleSet
{
    /// <summary>A step azonosítója – egyeznie kell a step agent config step mezőjével.</summary>
    public string Step { get; init; } = string.Empty;

    public ValidatorRules Rules { get; init; } = new();
}

public class ValidatorRules
{
    /// <summary>Mezők amiknek minden taskban szerepelniük kell.</summary>
    public List<string> RequiredFields { get; init; } = [];

    /// <summary>Task count korlátok.</summary>
    public TaskCountConstraint? TaskCount { get; init; }

    /// <summary>Szavak/kifejezések amik jelenléte constraint-sértést jelez.</summary>
    public List<string> ForbiddenKeywords { get; init; } = [];

    /// <summary>Fájlok amik nem szerepelhetnek "Affected files" alatt.</summary>
    public List<string> ForbiddenAffectedFiles { get; init; } = [];

    /// <summary>Elvárt mező-sorrend (opcionális).</summary>
    public List<string> FieldOrder { get; init; } = [];
}

public class TaskCountConstraint
{
    public int Min { get; init; } = 1;
    public int Max { get; init; } = int.MaxValue;
}