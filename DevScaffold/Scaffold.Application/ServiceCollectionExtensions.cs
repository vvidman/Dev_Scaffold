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
using Scaffold.Application.Artifacts;
using Scaffold.Application.Interfaces;

namespace Scaffold.Application;

/// <summary>
/// DI registrations for the Scaffold.Application layer.
///
/// Internal types (e.g. InferenceResultHandler) cannot be referenced
/// directly from outside – this method registers them without having
/// to widen their visibility.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Application layer's internal services.
    /// Called by Scaffold.CLI Program.cs when building the DI container.
    /// </summary>
    public static IServiceCollection AddScaffoldApplication(
        this IServiceCollection services)
    {
        services.AddSingleton<IRefinementStrategy, RefinementStrategy>();
        services.AddSingleton(new InferenceResultHandlerOptions());
        services.AddSingleton<IInferenceResultHandler, InferenceResultHandler>();
        services.AddSingleton<IMarkdownArtifactExtractor, DefaultMarkdownArtifactExtractor>();
        services.AddSingleton<IStepPostProcessor, TaskBreakdownSplitter>();
        services.AddSingleton<IStepPostProcessor, CodingOutputExtractor>();
        return services;
    }
}