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

namespace Scaffold.ServiceHost.Abstractions;

/// <summary>
/// Inference életciklus eseményeinek publikálása.
/// Az InferenceWorker csak ezt az interfészt látja –
/// nem tudja hogy a service réteg milyen eseményeket küld.
/// </summary>
public interface IInferenceEventPublisher
{
    Task PublishInferenceStartedAsync(
        string requestId,
        string stepId,
        string modelAlias,
        CancellationToken ct = default);

    Task PublishInferenceProgressAsync(
        string requestId,
        string stepId,
        uint elapsedSeconds,
        string statusMessage,
        CancellationToken ct = default);

    Task PublishInferenceCompletedAsync(
        string requestId,
        string stepId,
        string outputFilePath,
        uint elapsedSeconds,
        uint tokensGenerated,
        CancellationToken ct = default);

    Task PublishInferenceCancelledAsync(
        string requestId,
        string stepId,
        CancellationToken ct = default);

    Task PublishInferenceFailedAsync(
        string requestId,
        string stepId,
        string errorMessage,
        CancellationToken ct = default);
}