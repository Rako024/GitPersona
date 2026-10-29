# GitPersona

GitPersona bir Windows kompüterində bir neçə GitHub hesabını idarə etmək üçündür. Hər repo üçün düzgün SSH key, düzgün `user.email` və düzgün hesabı avtomatik seçir.

> **Status:** 1–3-cü mərhələlər hazırdır: skelet; profillər, SSH key və config yazılması; GitHub-a giriş və key yükləmə. Planı və mərhələləri [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) faylında oxuya bilərsiniz.

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

Testlərin bir hissəsi temp qovluqda real Git for Windows və OpenSSH ilə işləyir. Bu testlər sizin öz konfiqurasiyanızı nə oxuyur, nə də dəyişir.

### Sandbox-da sınaq

Tətbiqi real `~/.ssh` və `~/.gitconfig` fayllarınıza toxunmadan sınamaq üçün iki environment dəyişəni var:

```powershell
$env:GITPERSONA_HOME_DIR = "$env:TEMP\gp-sandbox\home"   # ~/.ssh və ~/.gitconfig əvəzinə
$env:GITPERSONA_DATA_DIR = "$env:TEMP\gp-sandbox\data"   # %APPDATA%\GitPersona əvəzinə
dotnet run --project src/GitPersona.App
```

## İstifadə

### Profil yaratmaq

1. **Profillər → Yeni profil** seçin. Göstərilən addan profil ID-si (`is-hesabi`) və SSH alias (`github-is-hesabi`) avtomatik törədilir, onları dəyişə bilərsiniz.
2. Git `user.name`, `user.email` və GitHub istifadəçi adını daxil edin.
3. **Yeni key yarat…** düyməsi ilə `~/.ssh/id_ed25519_<id>` faylını yaradın, və ya **Mövcud key-i seç…** ilə əvvəlki key-i seçin.
4. **Qovluqlar** bölməsinə əlavə etdiyiniz qovluqlardakı bütün repo-lar bu identity ilə commit edir. **Owner-lər** isə 4-cü mərhələdəki ağıllı clone üçündür.
5. **Yadda saxla** basın. GitPersona yazacağı faylların diff-ini göstərir. **Tətbiq et** düyməsinə basmasanız, heç bir fayl dəyişmir.

### GitHub OAuth App (bir dəfəlik)

"GitHub ilə daxil ol" funksiyası öz OAuth App-inizdən istifadə edir. Onu bir dəfə yaratmaq kifayətdir, bütün hesablarınız üçün işləyir — hər hesab tətbiqi ayrıca təsdiqləyir.

1. Ayarlar → GitHub → **OAuth App qeydiyyatı…** düyməsini basın (və ya <https://github.com/settings/applications/new> səhifəsini açın).
2. Sahələri doldurun:
   - **Application name:** `GitPersona`
   - **Homepage URL:** istənilən ünvan, məsələn `https://github.com/<username>`
   - **Authorization callback URL:** `http://127.0.0.1`. Device Flow bu ünvanı istifadə etmir, amma sahəni boş qoymaq olmur.
3. **Register application** basın, açılan səhifədə **Enable Device Flow** seçimini işarələyin və **Update application** basın.
4. **Client ID**-ni kopyalayıb GitPersona → Ayarlar → GitHub → *OAuth App client ID* sahəsinə yapışdırın. **Client secret yaratmayın**, o lazım deyil.

### Key-i GitHub-a yükləmək

1. Profildə **GitHub-a yüklə…** → **GitHub ilə daxil ol** seçin.
2. **Kodu kopyala və GitHub-ı aç** basın, kodu brauzerdə daxil edib təsdiqləyin.

   > Brauzerdə **profilin aid olduğu hesabla** daxil olmalısınız. Başqa hesab açıqdırsa, linki gizli (private) pəncərədə açın. GitPersona daxil olunan hesabı profilin GitHub istifadəçi adı ilə müqayisə edir və uyğun deyilsə dayanır.
3. Key hesaba yüklənir. Key hesabda artıq varsa, ikinci dəfə yüklənmir.
4. Hesabın təsdiqlənmiş email-lərindən və ya `noreply` ünvanından birini commit email-i kimi seçin. **Hazırdır** basın, sonra profildə **Yadda saxla** basın.

Brauzer əvəzinə personal access token da istifadə edə bilərsiniz. Lazım olan icazələr:

- **classic token:** `write:public_key` və `user:email` scope-ları;
- **fine-grained token:** *Git SSH keys: Read and write* və *Email addresses: Read-only* icazələri.

Token yalnız Windows Credential Manager-də `GitPersona:github.com:<login>` adı ilə saxlanılır (İdarəetmə paneli → Credential Manager → Windows Credentials). Onu redaktordakı **Girişi unut** düyməsi ilə silə bilərsiniz.

Təşkilatınız SAML SSO istifadə edirsə, key-i GitHub-da *Settings → SSH and GPG keys → Configure SSO* bölməsində həmin təşkilat üçün ayrıca təsdiqləməlisiniz.

Qoşulmanı yoxlayın:

```powershell
ssh -T git@github-<id>
```

Cavabda `Hi <username>!` görünməlidir. Bundan sonra remote-u `git@github-<id>:owner/repo.git` olan istənilən repo, harada yerləşməsindən asılı olmayaraq, bu profilin email-i ilə commit edəcək.

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
| `tests/GitPersona.Core.Tests` | Core üçün xUnit testləri (temp qovluqda real git/ssh ilə inteqrasiya testləri daxil) |
| `tests/GitPersona.App.Tests` | ViewModel məntiqinin testləri (məs. GitHub qoşulma axını) |
| `tools/New-AppIcon.ps1` | Tətbiq ikonunu yenidən yaradır |
