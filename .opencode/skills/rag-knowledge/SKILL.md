---
name: rag-knowledge
description: Use when working with the RAG knowledge base of the StudentManagement solution — adding knowledge documents, changing retrieval/chunking behavior, ChromaDB, embeddings, or the AppKnowledgeService/SemanticChunker. Trigger on "add knowledge doc", "RAG", "knowledge base", "AppKnowledge", "ChromaDB", "embedding", "chunking".
---

# RAG Knowledge Base (StudentManagement)

The AI chat answers questions about the system using Retrieval-Augmented Generation over embedded knowledge documents.

## Pipeline

```
AppKnowledge/** (embedded resources)
  → IDocumentParser (Txt/Docx/Pdf/Excel, selected by extension)
  → SemanticChunker (paragraphs → topic-shift aware KnowledgeChunks)
  → IEmbeddingGenerator (Chat:Features:EmbeddingModel, default nomic-embed-text)
  → ChromaDB upsert (Chroma:CollectionName = "student-management")
  → query time: SearchRelevantChunks(query, MaxRagChunks)  [distance < 0.7]
```

## Adding a knowledge document (most common task)

1. Drop the file into `StudentManagement.Application/AppKnowledge/` — supported: `.md`, `.txt`, `.docx`, `.pdf`, `.xlsx`/`.xls`.
2. It is auto-included as an embedded resource (no csproj change; glob already configured).
3. Restart the app and ask the chat a question about the new content. (Optional: `AppKnowledgeService.InitializeAsync()` is called on first chat use.)

## Tuning retrieval

- **Relevance threshold:** `dist < 0.7` in `AppKnowledgeService.SearchRelevantChunks` (in `StudentManagement.Application/Services/AppKnowledgeService.cs`). Lower = stricter.
- **Chunks returned:** `Chat:Features:MaxRagChunks` (default 3) in `appsettings.json`.
- **Topic-shift detection:** `SemanticChunker` splits on embedding cosine similarity `< 0.55` between sliding windows (`StudentManagement.Application/Services/SemanticChunker.cs`).
- **Include full docs in prompt:** `Chat:Features:IncludeFullDocumentation` (true in Production) vs. RAG-only (`IncludeRag`).

## Infrastructure

- **ChromaDB** must be running at `Chroma:Endpoint` (`http://localhost:8000`) — start it separately (`chroma run` / Docker). Collection `student-management` is created on init.
- **Ollama** must serve the embedding model (`Chat:Features:EmbeddingModel`).
- **Singleton scopes:** `AppKnowledgeService`, `SemanticChunker`, `ChromaClient`, parsers, embedding generator are all Singleton (see `ChatClientServiceCollectionExtensions.cs`) — preserve this.

## Testing

`AppKnowledgeServiceTests` loads embedded resources and asserts documentation text contains key terms. If you add a doc, consider extending these tests.

## Verify

`dotnet build` + `dotnet test`. Live RAG check requires Ollama + ChromaDB running and a chat question whose answer appears only in the new doc.
