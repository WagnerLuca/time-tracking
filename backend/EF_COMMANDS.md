# Entity Framework Commands for Time Tracking API

## Automatic Migration on Startup

The application **automatically applies all pending migrations** when it starts:
- **PostgreSQL**: Calls `context.Database.Migrate()` — creates the DB and applies all pending migrations.
- **InMemory** (dev): Calls `context.Database.EnsureCreated()` — creates schema without migrations.

This means you only need to **create** migrations during development. They will be applied automatically when the app starts.

## Initial Setup Commands

### 1. Install EF Core Tools (if not already installed)
```bash
dotnet tool install --global dotnet-ef
```

### 2. Create Initial Migration
```bash
cd backend
dotnet ef migrations add InitialCreate
```

> **Note**: A `DesignTimeDbContextFactory` is used so that `dotnet ef` always uses the Npgsql provider for migration generation, even when the app is configured for InMemory in development.

## Development Workflow

### Add Migration for New Changes
```bash
dotnet ef migrations add DescriptiveName
dotnet ef database update
```

### View Migration SQL (without applying)
```bash
dotnet ef migrations script
```

### Remove Last Migration (if not applied to database)
```bash
dotnet ef migrations remove
```

### Reset Database (Development Only)
```bash
dotnet ef database drop
dotnet ef database update
```

## Production Deployment

### Generate SQL Script for Production
```bash
dotnet ef migrations script --output migrations.sql
```

### Update Production Database
```bash
dotnet ef database update --connection "Host=prod-server;Database=timetracking_db;Username=prod_user;Password=prod_pass"
```

## Database Connection Strings

### Development (Local)
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=timetracking_db;Username=user;Password=pass"
}
```

### Production (Environment Variable)
```bash
DATABASE_URL="Host=prod-server;Database=timetracking_db;Username=prod_user;Password=prod_pass"
```

## Troubleshooting

### If migrations fail:
1. Check database connection
2. Ensure PostgreSQL is running
3. Verify user permissions
4. Check for naming conflicts

### Reset migrations completely:
```bash
rm -rf Migrations/
dotnet ef migrations add InitialCreate
dotnet ef database drop
dotnet ef database update
```