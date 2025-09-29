# Time Tracking Application

A full-stack time tracking application with account handling and multi-organization support.

## 🏗️ Architecture

- **Backend**: ASP.NET Core 9.0 Web API
- **Frontend**: SvelteKit with TypeScript (SSR)
- **Database**: PostgreSQL 16
- **Infrastructure**: Docker containers with multi-stage builds
- **CI/CD**: GitHub Actions with automated testing and deployment

## 🚀 Quick Start

### Prerequisites

- Docker and Docker Compose (for database and production)
- Node.js 20+ (required for frontend development)
- .NET 9.0 SDK (required for backend development)

### Development Environment

#### Recommended: Local Development with Docker Database

For the best development experience with hot reload and instant feedback:

1. Clone the repository:
```bash
git clone https://github.com/wagnerluca/time-tracking.git
cd time-tracking
```

2. Start the database only:
```bash
docker-compose up db -d
```

3. Start the backend with hot reload:
```bash
cd backend
dotnet restore
dotnet run --watch
```

4. Start the frontend with Vite dev server (in a new terminal):
```bash
cd frontend
npm install
npm run dev
```

5. Access the applications:
   - Frontend: http://localhost:5173 (Vite dev server with HMR)
   - Backend API: http://localhost:7000 (with hot reload)
   - API Documentation: http://localhost:7000/swagger
   - Database: localhost:5432

#### Alternative: Full Docker Development

For testing production-like behavior or team consistency:

```bash
docker-compose up -d
```

Access URLs:
- Frontend: http://localhost:3000
- Backend API: http://localhost:7000
- API Documentation: http://localhost:7000/swagger

### Production Deployment

#### Using Deployment Scripts

**Windows (PowerShell):**
```powershell
.\deploy.ps1 [version] [environment]
```

**Linux/macOS:**
```bash
./deploy.sh [version] [environment]
```

#### Manual Deployment

1. Create environment file:
```bash
cp .env.production .env.production.local
# Edit .env.production.local with your values
```

2. Set image versions:
```bash
export BACKEND_IMAGE="ghcr.io/wagnerluca/time-tracking-backend:latest"
export FRONTEND_IMAGE="ghcr.io/wagnerluca/time-tracking-frontend:latest"
```

3. Deploy:
```bash
docker-compose -f docker-compose.production.yml --env-file .env.production.local up -d
```

## 🛠️ Development

### Backend Development

```bash
cd backend
dotnet restore
dotnet run
```

### Frontend Development

```bash
cd frontend
npm install
npm run dev
```

### Building Images Locally

```bash
# Build all services
docker-compose -f docker-compose.dev.yml build

# Build specific service
docker-compose -f docker-compose.dev.yml build backend
docker-compose -f docker-compose.dev.yml build frontend
```

## 📋 Environment Configuration

### Development (.env files)

Environment variables are configured in docker-compose files for development.

### Production

Copy `.env.production` to `.env.production.local` and configure:

- `POSTGRES_PASSWORD`: Database password
- `POSTGRES_USER`: Database username
- `POSTGRES_DB`: Database name
- `DATABASE_URL`: Complete database connection string
- `ASPNETCORE_ENVIRONMENT`: Set to "Production"

## 🔄 CI/CD Pipeline

The GitHub Actions workflow automatically:

1. **Test**: Runs backend and frontend tests
2. **Build**: Creates optimized Docker images
3. **Deploy**: Pushes images to GitHub Container Registry

### Triggering Deployment

Push to main branch or create a release tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 📊 Monitoring and Logs

### View Application Logs

```bash
# All services
docker-compose -f docker-compose.production.yml logs -f

# Specific service
docker-compose -f docker-compose.production.yml logs -f backend
docker-compose -f docker-compose.production.yml logs -f frontend
```

### Health Checks

- Backend: http://localhost:7000/health
- Frontend: http://localhost:3000/health

### Service Status

```bash
docker-compose -f docker-compose.production.yml ps
```

## 🐛 Troubleshooting

### Container Issues

```bash
# Restart services
docker-compose -f docker-compose.production.yml restart

# Rebuild and restart
docker-compose -f docker-compose.production.yml up -d --build

# View detailed logs
docker-compose -f docker-compose.production.yml logs --tail=100
```

### Database Issues

```bash
# Connect to database
docker-compose -f docker-compose.production.yml exec db psql -U timetracking_user -d timetracking_db

# Reset database
docker-compose -f docker-compose.production.yml down -v
docker-compose -f docker-compose.production.yml up -d
```

### Image Issues

```bash
# Pull latest images
docker pull ghcr.io/wagnerluca/time-tracking-backend:latest
docker pull ghcr.io/wagnerluca/time-tracking-frontend:latest

# Login to registry
docker login ghcr.io
```

## 🔒 Security

- Non-root users in production containers
- Environment variables for sensitive configuration
- HTTPS ready (configure reverse proxy)
- Security scanning can be added back when needed

## 🤝 Contributing

1. Clone the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Commit changes: `git commit -m 'Add amazing feature'`
4. Push to branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

## 📄 License

This project is licensed under the Apache 2.0 License - see the [LICENSE](LICENSE) file for details.
