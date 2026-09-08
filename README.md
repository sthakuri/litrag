# LitRAG

A local, offline literature-review assistant. Upload research paper PDFs into a personal library,
browse and search them, then open a paper next to a chat panel and ask grounded questions about it —
all powered by a locally running [Ollama](https://ollama.com) model. No cloud calls.

## Features

- **Upload & validate**: drop in a PDF with tags and a source URL. The app extracts the text, checks
  it actually looks like a published research paper (heuristics + an Ollama classification pass), and
  shows a clear rejection message if it isn't — instead of silently accepting anything.
- **Library & search**: browse uploaded papers and filter by title, author, venue, or tag.
- **Split-view reading + chat**: open a paper to see it rendered on the left (native browser PDF
  viewer) with a chat panel on the right, scoped to that paper only. Answers are grounded in the
  paper's content via retrieval-augmented generation and cite source page numbers.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) installed and running locally, with two models pulled:
  ```
  ollama pull llama3.1:8b
  ollama pull nomic-embed-text
  ```

## Running

```
dotnet run --project src/LitRag.Web
```

Then open the URL printed in the console (e.g. `http://localhost:5246`).

Model names and the Ollama endpoint are configurable in
[`src/LitRag.Web/appsettings.json`](src/LitRag.Web/appsettings.json) under the `Ollama` section, in
case you'd rather use different local models.

If Ollama isn't running, the library and search still work; a banner will let you know that upload
validation and chat are unavailable until it's started.

## How it works

- **Storage**: uploaded PDFs are copied into `src/LitRag.Web/wwwroot/library/`; metadata, tags, text
  chunks, embeddings, and chat history live in a local SQLite database at
  `src/LitRag.Web/App_Data/litrag.db` (both are git-ignored — they're per-machine data, not source).
- **PDF text extraction**: [Docnet.Core](https://github.com/GowenGit/docnet) (a wrapper around
  Google's PDFium).
- **RAG**: on upload, extracted text is chunked (~1000 characters, ~150 character overlap, tagged
  with page numbers) and embedded via Ollama's embedding model. When you ask a question, it's
  embedded too, matched against that paper's chunks by cosine similarity, and the top matches are
  fed to the chat model as grounding context.

## Project layout

```
src/LitRag.Web/
  Components/Pages/Library.razor        upload form, search, paper list (home page)
  Components/Pages/PaperWorkspace.razor  /paper/{id}: split view — PDF left, chat right
  Services/Ollama/                       Ollama REST client (chat streaming, embeddings, JSON generate)
  Services/Pdf/                          PDF text extraction
  Services/Validation/                   "is this a research paper?" classification
  Services/Rag/                          chunking, embeddings, vector search, RAG chat orchestration
  Services/Library/                      paper CRUD + search
  Data/                                  EF Core DbContext and entities
```

## A note on dependencies

While setting this up, the `UglyToad.PdfPig` NuGet package — normally the default choice for PDF
text extraction in .NET — was found to be a likely-hijacked package (its NuGet owner is not the
original maintainer, and its "latest" version is a nonstandard release with almost all of its
downloads). It was deliberately avoided in favor of `Docnet.Core`, which is legitimately maintained.
Worth keeping in mind if you add PDF-related dependencies to this project later.
