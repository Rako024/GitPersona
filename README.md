# GitPersona

GitPersona bir Windows kompüterində bir neçə GitHub hesabını idarə etmək üçündür. Hər repo üçün düzgün SSH key, düzgün `user.email` və düzgün hesabı avtomatik seçir.

> **Status:** 1-ci mərhələ (skelet) hazırdır. Planı və mərhələləri [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) faylında oxuya bilərsiniz.

## Tələblər

- Windows 10 və ya 11
- Build üçün [.NET 10 SDK](https://dotnet.microsoft.com/download). Hazır exe-ni işlətmək üçün .NET lazım deyil.
- [Git for Windows](https://git-scm.com/download/win)

## Build, test və işə salma

```powershell
dotnet build GitPersona.slnx
dotnet test GitPersona.slnx
dotnet run --project src/GitPersona.App
```

Əlavə arqumentlər:

- `--minimized` tətbiqi pəncərəsiz, yalnız tray-də açır. "Windows ilə açılsın" seçimi də bu arqumentdən istifadə edir.

## Publish (tək `.exe`)

```powershell
dotnet publish src/GitPersona.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Nəticə `publish\GitPersona.exe` faylıdır (~62 MB, .NET runtime içindədir). Tətbiq admin hüququ tələb etmir.

## Məlumatlar harada saxlanılır

| Nə | Harada |
|---|---|
| Profillər | `%APPDATA%\GitPersona\profiles.json` |
| Ayarlar | `%APPDATA%\GitPersona\settings.json` |
| Loglar (14 gün saxlanılır) | `%APPDATA%\GitPersona\logs\` |
| "Windows ilə açılsın" | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GitPersona` |

Token-lər yalnız Windows Credential Manager-də saxlanılır. GitPersona `~/.ssh/config` və `~/.gitconfig` fayllarında yalnız öz marker blokunu dəyişir:

```
# >>> GitPersona managed >>>
...
# <<< GitPersona managed <<<
```

Hər dəyişiklikdən əvvəl diff göstərilir və faylın yanında `.yyyyMMdd-HHmmss.bak` backup-ı yaradılır.

## Layihənin quruluşu

| Qovluq | Məzmun |
|---|---|
| `src/GitPersona.Core` | UI-dan asılı olmayan məntiq: fayl yazma, proseslər, profillər |
| `src/GitPersona.App` | WPF tətbiqi (MVVM, WPF-UI, tray) |
| `tests/GitPersona.Core.Tests` | xUnit testləri |
| `tools/New-AppIcon.ps1` | Tətbiq ikonunu yenidən yaradır |
