using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

public interface IModelRouter
{
    IAgentModel Select(AgentRequest request);
}
