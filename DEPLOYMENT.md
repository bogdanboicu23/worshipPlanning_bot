# Worship Planner Bot - Deployment Guide

## Deploying to Azure App Service

### Prerequisites
1. GitHub Student Developer Pack (for free Azure credits)
2. Azure account activated with student credits
3. Your Telegram Bot Token from @BotFather

### Step-by-Step Deployment

#### 1. Activate Azure Student Benefits
1. Go to [Azure for Students](https://azure.microsoft.com/en-us/free/students/)
2. Sign in with your GitHub Student account
3. Claim your $100 credit

#### 2. Create Azure App Service
1. Go to [Azure Portal](https://portal.azure.com)
2. Click "Create a resource" → "Web App"
3. Configure:
   - **Subscription**: Azure for Students
   - **Resource Group**: Create new → `worship-bot-rg`
   - **Name**: `worship-planner-bot` (must be unique)
   - **Publish**: Code
   - **Runtime stack**: .NET 8
   - **Operating System**: Linux
   - **Region**: Choose nearest to you
   - **Pricing Plan**: F1 (Free tier) or B1 (Basic)

4. Click "Review + create" → "Create"

#### 3. Configure Environment Variables
In Azure Portal:
1. Go to your App Service
2. Settings → Configuration → Application settings
3. Add the following:
   - `BotConfiguration__BotToken`: Your Telegram bot token
   - `ASPNETCORE_ENVIRONMENT`: Production

4. Save changes

#### 4. Setup Continuous Deployment
1. In your App Service, go to "Deployment Center"
2. Choose "GitHub" as source
3. Authorize and select your repository
4. Azure will create a workflow file automatically, OR
5. Use the manual method below:

##### Manual GitHub Actions Setup:
1. In Azure Portal → Your App Service → "Deployment Center" → "Manage publish profile"
2. Download the publish profile
3. In GitHub repository → Settings → Secrets → Actions
4. Create new secret: `AZURE_WEBAPP_PUBLISH_PROFILE`
5. Paste the entire content of the downloaded publish profile

#### 5. Deploy Your Bot
1. Commit and push your code to GitHub:
```bash
git add .
git commit -m "Initial deployment setup"
git push origin main
```

2. The GitHub Action will automatically:
   - Build your application
   - Run tests (if any)
   - Deploy to Azure

3. Monitor deployment:
   - Check GitHub Actions tab in your repository
   - Check Azure Portal → Your App Service → "Deployment Center"

#### 6. Database Persistence
The bot uses SQLite which stores data locally. For Azure:
- The database file (`worshipbot.db`) is stored in the app's directory
- **Important**: On Free/Basic tier, database may be reset on restart
- For production, consider:
  - Using Azure SQL Database (free tier available)
  - Or upgrading to a persistent storage plan

#### 7. Verify Deployment
1. In Azure Portal → Your App Service → Overview
2. Copy the URL (e.g., `https://worship-planner-bot.azurewebsites.net`)
3. Check logs: "Monitoring" → "Log stream"
4. Test your bot in Telegram

### Alternative Free Hosting Options

#### Railway.app (Simpler)
1. Connect GitHub repo at [railway.app](https://railway.app)
2. Add environment variable: `BotConfiguration__BotToken`
3. Deploy automatically

#### DigitalOcean App Platform
1. Use your $200 student credit
2. Create new app from GitHub
3. Choose Basic plan ($5/month, covered by credits)
4. Add environment variables
5. Deploy

### Security Notes
- **NEVER** commit your bot token to GitHub
- Use environment variables for all secrets
- Keep `appsettings.json` with empty token in repository
- Use `appsettings.Production.json` for production config (without secrets)

### Monitoring
- Azure: Application Insights (free tier)
- Check logs regularly for errors
- Set up alerts for downtime

### Troubleshooting
1. **Bot not responding**: Check bot token in environment variables
2. **Database errors**: Ensure write permissions in app directory
3. **Deployment fails**: Check GitHub Actions logs
4. **App crashes**: Check Azure Log Stream

### Costs
- **Azure Free Tier (F1)**: Always free, 60 CPU minutes/day
- **Azure Basic (B1)**: ~$13/month (covered by student credits)
- **Railway**: Free tier with limits, then $5/month
- **DigitalOcean**: $5/month (covered by $200 credit)

### Support
- [Azure Documentation](https://docs.microsoft.com/azure)
- [GitHub Actions Documentation](https://docs.github.com/actions)
- Your bot logs in Azure Portal → Log stream