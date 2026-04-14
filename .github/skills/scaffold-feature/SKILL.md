---
name: scaffold-feature
description: 'Scaffold a new feature end-to-end: model, service interface, service implementation, DI registration, EF Core migration, and Blazor page. Use when adding a new entity or CRUD feature to the app.'
argument-hint: 'Feature name (e.g., "Note" or "Project")'
---

# Scaffold Feature

Generates all layers for a new CRUD feature following this project's conventions.

## When to Use

- Adding a new entity/feature to the app (e.g., Notes, Projects, Tags)
- Need full stack: model → service → page wired together

## Procedure

1. **Get the feature name** from the user's input (e.g., "Note"). Derive:
   - Model class: `{Name}` (e.g., `Note`)
   - Service interface: `I{Name}Service`
   - Service class: `{Name}Service`
   - Page route: `/{name}s` (lowercase plural)
   - DbSet name: `{Name}s`

2. **Create the model** in `Models/{Name}.cs` following [model template](./templates/Model.cs.txt)
   - Always include `Id`, `UserId`, `CreatedAt` properties
   - Add `[Required]` and `[MaxLength]` annotations on string properties
   - Ask the user what properties the entity needs

3. **Create the service interface** in `Services/I{Name}Service.cs` following [interface template](./templates/IService.cs.txt)
   - Standard CRUD: GetAll, GetById, Create, Update, Delete
   - All methods accept `userId` for data scoping

4. **Create the service implementation** in `Services/{Name}Service.cs` following [service template](./templates/Service.cs.txt)
   - Use primary constructor with `ApplicationDbContext`
   - Always filter by `userId`
   - Use `Include()` for navigation properties

5. **Add DbSet** to `Data/ApplicationDbContext.cs`:
   ```csharp
   public DbSet<{Name}> {Name}s => Set<{Name}>();
   ```

6. **Register service** in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<I{Name}Service, {Name}Service>();
   ```

7. **Create EF migration**:
   ```
   dotnet ef migrations add Add{Name}Model
   ```

8. **Create the Blazor page** in `Components/Pages/{Name}s.razor` following [page template](./templates/Page.razor.txt)
   - Use `@attribute [Authorize]` and `@rendermode InteractiveServer`
   - Include list view, create/edit modal, delete
   - Inject the service interface

9. **Add nav link** in `Components/Layout/NavMenu.razor`

## Constraints

- DO NOT create a repository layer — services use DbContext directly
- DO NOT add client-side SignalR connections
- All data queries MUST filter by `userId`
