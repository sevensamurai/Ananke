// Request payload sent from the browser to POST /api/chat.
record ChatRequest(string Message, List<ChatHistoryMessage>? History);

// A single message in the conversation history (role = "user" or "assistant").
record ChatHistoryMessage(string Role, string Content);
