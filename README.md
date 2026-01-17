# Worship Planner Bot

A Telegram bot for managing worship service planning and attendance tracking with role-based organization.

## Features

- **Event Management**: Create and manage worship service events
- **Role-Based Organization**: Assign team members to specific roles (Vocals, Guitar, Bass, Drums, Keyboard, Sound Tech, Media, Prayer)
- **Attendance Tracking**: Interactive RSVP system with Yes/No/Maybe options
- **Role Visualization**: See attendance organized by team roles
- **User Registration**: Self-service role selection for team members
- **Admin Controls**: Special commands for administrators

## Prerequisites

- .NET 10.0 SDK or later
- Telegram Bot Token (from [@BotFather](https://t.me/botfather))

## Setup Instructions


### 1. Configure the Application

1. Clone the repository
2. Navigate to the project directory
3. Update `appsettings.json` with your bot token:

```json
{
  "BotConfiguration": {
    "BotToken": "YOUR_BOT_TOKEN_HERE",
    "WebhookUrl": "",
    "UseWebhook": false
  }
}
```

### 2. Run the Application

```bash
# Navigate to the API project
cd WorshipPlannerBot.Api

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the bot
dotnet run
```

The bot will start polling for updates and create a SQLite database (`worshipbot.db`) automatically.

## Bot Commands

### User Commands
- `/start` - Start interaction with the bot
- `/register` - Set up your profile and select roles
- `/myroles` - View and manage your assigned roles
- `/events` - View upcoming worship services
- `/help` - Show available commands

### Admin Commands
- `/newevent` - Create a new worship service event
- `/admin` - Access admin panel
- `/setrole @username role` - Assign role to a user
- `/makeadmin @username` - Grant admin privileges
- `/removeadmin @username` - Revoke admin privileges

## Creating Events

Admins can create events using `/newevent`. The bot will ask for event details in this format:

```
Event Title
DD/MM/YYYY HH:MM
Location
Description (optional)
```

Example:
```
Sunday Worship Service
25/01/2025 10:30
Main Hall
Regular Sunday morning worship with communion
```

## Role Management

Available roles:
- 🎤 Vocals - Lead and backing vocals
- 🎸 Guitar - Acoustic and electric guitar
- 🎸 Bass - Bass guitar
- 🥁 Drums - Drums and percussion
- 🎹 Keyboard - Piano and keyboards
- 🎧 Sound Tech - Sound mixing and audio
- 📹 Media - Visuals and streaming
- 🙏 Prayer - Prayer team

Users can select multiple roles during registration.

## Attendance Tracking

When an event is posted, users can mark their attendance using inline buttons:
- ✅ Yes - Confirmed attendance
- ❌ No - Cannot attend
- ❓ Maybe - Uncertain

The bot displays attendance organized by roles, showing which positions are covered for each service.

## Database

The bot uses SQLite for data storage. The database file (`worshipbot.db`) is created automatically on first run and includes:
- User profiles and Telegram IDs
- Role assignments
- Worship service events
- Attendance records

## Making the First Admin

The first admin needs to be set manually in the database:

1. Start the bot and send `/start` to register yourself
2. Stop the bot (Ctrl+C)
3. Use a SQLite browser or command line:

```sql
UPDATE Users SET IsAdmin = 1 WHERE Username = 'your_telegram_username';
```

4. Restart the bot

## Deployment Options

### Local Development
Use the polling mode (default) for local testing and development.

### Production (Webhook)
For production deployment, you can use webhooks:

1. Set up a public HTTPS endpoint
2. Update `appsettings.json`:
```json
{
  "BotConfiguration": {
    "WebhookUrl": "https://yourdomain.com/bot",
    "UseWebhook": true
  }
}
```

## Troubleshooting

- **Bot not responding**: Check that the bot token is correct in `appsettings.json`
- **Database errors**: Ensure write permissions for the application directory
- **Commands not working**: Verify user registration with `/start` first

## License

This project is provided as-is for worship planning purposes.
