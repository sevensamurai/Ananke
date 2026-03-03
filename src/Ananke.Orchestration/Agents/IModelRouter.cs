namespace Ananke.Orchestration.Agents;

public interface IModelRouter
{
    IAgentModel Select(AgentRequest request);
}
