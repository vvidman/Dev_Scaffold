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

using Microsoft.Extensions.DependencyInjection;
using Scaffold.Application.Interfaces;

namespace Scaffold.Application;

/// <summary>
/// Scaffold.Application réteg DI regisztrációi.
///
/// Az internal típusok (pl. InferenceResultHandler) kívülről nem
/// hivatkozhatók közvetlenül – ez a metódus regisztrálja őket
/// anélkül hogy a láthatóságukat fel kellene oldani.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Regisztrálja az Application réteg belső szolgáltatásait.
    /// A Scaffold.CLI Program.cs hívja a DI konténer felépítésekor.
    /// </summary>
    public static IServiceCollection AddScaffoldApplication(
        this IServiceCollection services)
    {
        services.AddSingleton<IRefinementStrategy, RefinementStrategy>();
        services.AddSingleton<IInferenceResultHandler, InferenceResultHandler>();
        services.AddSingleton<IStepPostProcessor, TaskBreakdownSplitter>();
        return services;
    }
}