# 🚂 Deploy to Railway - Quick Guide (2 minutes!)

## Why Railway?
- ✅ **Easiest deployment** - Just connect GitHub
- ✅ **Free tier**: 500 hours/month + $5 credit
- ✅ **Persistent SQLite storage**
- ✅ **Auto-deploys** on git push
- ✅ **No credit card required** to start

## 🚀 Quick Deploy Steps

### 1. Sign Up
Go to [railway.app](https://railway.app?referralCode=student) and sign in with GitHub

### 2. Create New Project
1. Click **"New Project"**
2. Choose **"Deploy from GitHub repo"**
3. Select your `WorshipPlannerBot` repository
4. Railway will auto-detect it's a .NET project

### 3. Add Environment Variables
In Railway dashboard → Variables tab, add:
```
BotConfiguration__BotToken = YOUR_TELEGRAM_BOT_TOKEN
ASPNETCORE_ENVIRONMENT = Production
```

### 4. Deploy!
Railway will automatically:
- Build your .NET app
- Deploy it
- Give you a URL (you won't need it for Telegram bot)

### 5. Enable Persistent Storage (Important!)
1. In Railway dashboard → Settings
2. Enable **"Persistent Storage"**
3. Mount path: `/app/data`
4. Update your connection string in Railway Variables:
```
ConnectionStrings__DefaultConnection = Data Source=/app/data/worshipbot.db
```

## 📱 Test Your Bot
Open Telegram and message your bot - it should respond!

## 🔧 Monitor & Debug
- **Logs**: Railway dashboard → Logs tab
- **Metrics**: Railway dashboard → Metrics tab
- **Deploy status**: Railway dashboard → Deployments tab

## 💰 Costs
- **Free**: 500 hours/month (enough for 24/7 bot)
- **After free tier**: ~$5/month for a bot
- **Student discount**: Available through GitHub Student Pack

## 🆘 Troubleshooting

**Bot not responding?**
- Check Variables tab - ensure bot token is correct
- Check Logs tab for errors

**Database errors?**
- Ensure Persistent Storage is enabled
- Check the mount path is `/app/data`

**Deploy failed?**
- Check build logs
- Ensure all files are committed to GitHub

## Alternative: DigitalOcean App Platform

If Railway doesn't work, try DigitalOcean:

### DigitalOcean Setup ($200 free credit with Student Pack)
1. Go to [digitalocean.com](https://www.digitalocean.com/github-students)
2. Sign up with GitHub Student Pack ($200 credit)
3. Create App → From GitHub
4. Choose **Basic Plan** ($5/month)
5. Add environment variables (same as Railway)
6. Deploy!

### DigitalOcean Advantages:
- $200 credit (40 months of hosting!)
- More professional platform
- Better for learning DevOps
- Includes database options

## Alternative: Render.com

### Render Setup (Free tier with limitations)
1. Go to [render.com](https://render.com)
2. New → Web Service → Connect GitHub
3. Choose your repo
4. **Free tier**: Spins down after 15 min inactivity
5. **Paid**: $7/month for always-on

### Render Limitations:
- Free tier sleeps (slow first response)
- No persistent storage on free tier
- Need paid plan for 24/7 bot

## Alternative: Fly.io

### Fly.io Setup (Free tier available)
1. Install flyctl CLI
2. Run `fly launch` in project directory
3. Configure with `fly.toml`
4. Deploy with `fly deploy`

### Fly.io Advantages:
- Free tier includes persistent storage
- Global deployment
- Good for learning modern deployment

---

## Recommended Order to Try:
1. **Railway** - Easiest, best free tier
2. **DigitalOcean** - Use student credits
3. **Fly.io** - Good free tier
4. **Render** - Only if others fail

## Need Help?
- Railway Discord: [discord.gg/railway](https://discord.gg/railway)
- Check logs in your platform's dashboard
- Database issues? Ensure persistent storage is configured