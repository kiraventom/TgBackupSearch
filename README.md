# Telegram channel media OCR

### Features
- Runs OCR on the visual media in the channel: photos, videos (paid media are not supported)
  - Every photo is recognized with `tesseract`
  - For every video 10 frames are fetched with `ffmpeg`, then recognized with `tesseract` 
- Two modes of recognition:
  - Online recognition: can be ran against the channel directly; app downloads media from channel one-by-one, runs recognitions and deletes the files
  - Offline recognition: can be ran the offline backup created with [TgChannelBackup](https://github.com/kiraventom/TgChannelBackup)

### Requirements
- .NET 10 or higher
- `tesseract`
- `ffmpeg`, `ffprobe`
- [TgChannelBackup](https://github.com/kiraventom/TgChannelBackup)
- [TgChannelLib](https://github.com/kiraventom/TgChannelLib)

### Run
#### Online mode (using Channel ID)
1. Follow the instructions for setting up .env file from [TgChannelBackup](https://github.com/kiraventom/TgChannelBackup) README.
2. Run the application, providing the channel ID. Example: `dotnet run -- --channel 1006503122`
3. Application will create example `config.json` file if not present. You can configure Tesseract directories and languages via this file.
4. Recognition will start, starting from either the first avaiable post or first post without recognitions, depending on if the app was run previously.
5. After completing the recognition TgChannelRecognition will close itself.

#### Offline mode (using path to backup created with [TgChannelBackup](https://github.com/kiraventom/TgChannelBackup))
1. Run the application, providing the path to backup. Example: `dotnet run -- --channel ~/media/tg-backup/channel_1006503122`
2. Application will create example `config.json` file if not present. You can configure Tesseract directories and languages via this file.
3. Recognition will start, starting from either the first avaiable post or first post without recognitions, depending on if the app was run previously.
4. After completing the recognition TgChannelRecognition will close itself. 

### Troubleshooting
1. App will close if failing to find either `ffmpeg`, `ffprobe` or `tesseract-ocr`. Check `PATH` envvar and `config.json`.
2. App will close if failing to find `tessdata` folder. Check `config.json`.
3. (Online mode) App will close if Telegram API credentials were not provided or invalid. Check [TgChannelBackup](https://github.com/kiraventom/TgChannelBackup) README.
4. (Online mode) App will close if the account to which credentials were provided is not a member of a target channel.

### TODO
- Discussion group media recognition
- Audio recognition

### Generated files
TgChannelRecognize stores its database and logs at `~/.local/share/TgChannelRecognize`,  config at `~/.config/TgChannelRecognize`.

### Bugs
There are some, for sure.
