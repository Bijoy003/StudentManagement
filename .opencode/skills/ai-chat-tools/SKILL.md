---
name: ai-chat-tools
description: Use when adding, modifying, or debugging AI chat function tools, the ChatTools class, or extending what the chat assistant can do in the StudentManagement solution. Trigger on "add AI tool", "new chat tool", "ChatTools", "function calling", or "make the assistant able to X".
---

# Extending AI Chat Tools (StudentManagement)

The chat assistant answers data questions via AI function tools (`ChatService` collects them with `UseFunctionInvocation()`). Tools live in one place: `StudentManagement.Application/Services/ChatTools.cs`.

## Adding a new tool

1. **Open `StudentManagement.Application/Services/ChatTools.cs`.** Every tool is a public async method that returns JSON via the service layer.

2. **Pattern — copy an existing tool:**
   - `[Description("...")]` attribute (human-readable, what the tool does) — REQUIRED, drives the model's tool selection.
   - `[Description("...")]` on each parameter (the model uses these to fill args).
   - `CancellationToken cancellationToken = default` last parameter.
   - Call the appropriate service (`_studentService`, `_courseService`, `_enrollmentService`) — injected in the constructor.
   - Return `JsonSerializer.Serialize(result, JsonOptions)` with `WriteIndented = true`.
   - Project to anonymous objects (`new { s.Id, s.Name }`) — never serialize entities/navs directly (cycles/lazy-loading).

3. **Register it** in `GetTools()`:
   ```csharp
   public IList<AITool> GetTools() =>
   [
       // ... existing ...
       AIFunctionFactory.Create(MyNewToolAsync),
   ];
   ```

4. **If the tool needs new data:** add the method to the Domain repository interface + Infrastructure implementation (and Application service interface/impl if query composition is needed), following layering rules.

## Existing tools (9) for reference

`ListStudentsAsync`, `GetStudentByIdAsync`, `GetStudentsEnrolledInMoreThanAsync`, `ListCoursesAsync`, `GetCourseByIdAsync`, `GetStudentCountPerCourseAsync`, `ListEnrollmentsAsync`, `GetStudentsInCourseAsync`, `GetCoursesForStudentAsync`.

## Constraints

- Tools are **Scoped** (constructor-injected services are scoped) — keep `ChatTools`/`IChatService` Scoped in `Program.cs`.
- `[Description]` is the ONLY accepted doc style for tools — do not add XML doc comments.
- Do not add heavy logic in a tool; delegate to the service layer.
- To enable/disable tool usage in chat, use `Chat:Features:IncludeTools` in `appsettings.json` (no code change).

## Verify

`dotnet build` then `dotnet test` (ChatServiceTests asserts tools are attached to the reply). For a live check, Ollama must be running and `IncludeTools: true`.
