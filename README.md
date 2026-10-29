# GitPersona

GitPersona bir Windows kompüterində bir neçə GitHub hesabını idarə etmək üçündür. Hər repo üçün düzgün SSH key, düzgün `user.email` və düzgün hesabı avtomatik seçir.

> **Status:** 1–7-ci mərhələlər hazırdır: skelet; profillər, SSH key və config yazılması; GitHub-a giriş və key yükləmə; ağıllı clone; commit qoruyucusu; repo skaneri; tray və diaqnostika. Planı və mərhələləri [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) faylında oxuya bilərsiniz.

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

### Ağıllı clone

**Clone** səhifəsində və ya tray menyusundakı **URL-dən clone et…** ilə istifadə olunur. Tray bəndi buferdə GitHub URL-i varsa, onu avtomatik götürür. Aşağıdakı formatların hamısı qəbul olunur:

- `https://github.com/owner/repo` (brauzerdən kopyalanan `/tree/main/...` linkləri də);
- `git@github.com:owner/repo.git`;
- `ssh://...`;
- `owner/repo`;
- `git clone ...` əmri.

Profil bu ardıcıllıqla seçilir:

1. URL artıq profilin alias-ını istifadə edirsə (`git@github-work:...`), həmin profil.
2. Owner profilin **owner-lər** siyahısındadırsa, həmin profil.
3. Owner profilin GitHub istifadəçi adıdırsa, həmin profil.
4. Heç biri uyğun gəlmirsə, profil avtomatik seçilmir. Siz seçirsiniz və **"<owner> üçün həmişə bu profildən istifadə et"** seçimi ilə qaydanı yadda saxlaya bilərsiniz.

Repo `git@<alias>:owner/repo.git` ünvanından profilin default qovluğuna (və ya seçdiyiniz qovluğa) clone edilir. Clone bitəndən sonra GitPersona repo-da effektiv `user.email`-in profilin email-i olduğunu yoxlayır. Uğursuz və ya dayandırılmış clone yarımçıq qovluq qoymur.

### Commit qoruyucusu

**Ayarlar → Commit qoruyucusu** bölməsində aktiv edilir. GitPersona `~/.gitconfig`-ə `core.hooksPath` yazmazdan əvvəl diff göstərir. Aktiv olanda hər `git commit`-dən əvvəl yoxlanılır:

1. Repo-nun profili belə müəyyən edilir: remote-un SSH alias-ı, alias yoxdursa repo-nu əhatə edən ən dərin profil qovluğu.
2. git-in istifadə edəcəyi müəllif email-i həmin profilin email-i deyilsə, commit dayandırılır və həll yolu göstərilir.
3. Profillərinizdən kənar repo-lar yoxlanmır.

Bir dəfəlik yoxlamasız commit etmək üçün:

```bash
git commit --no-verify
```

Qeydlər:

- **Repo-ların öz hook-ları** (`.git/hooks/*`: husky v4, pre-commit, Git LFS, əl ilə yazılmış hook-lar) işləməyə davam edir. GitPersona onları eyni arqumentlər və stdin ilə çağırır.
  - Bu, client-side hook-lara aiddir. `reference-transaction`, `post-index-change` və server-side hook-lar sürət səbəbindən zəncirlənmir.
  - Əvvəlcədən global `core.hooksPath` qurmuşdunuzsa, həmin qovluğun hook-ları da çağırılır.
- **Öz `core.hooksPath`-ı olan repo-lar** (husky v5+) yoxlanmır, çünki lokal ayar global ayarı üstələyir.
- **Python pre-commit framework** `core.hooksPath` qurulu olanda `pre-commit install` əmrindən imtina edir. Onu Git Bash-də belə quraşdırın:

  ```bash
  GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=core.hooksPath GIT_CONFIG_VALUE_0= pre-commit install
  ```

  Quraşdırılmış hook GitPersona vasitəsilə işləyəcək.
- **Sürət:** Windows-da qoruyucu hər commit-ə təxminən 0,3 s əlavə edir.
- **Mesajların dili:** qoruyucunun mesajları interfeysin dilindədir.

### Repo skaneri

**Repo skaneri** səhifəsi bütün profil qovluqlarını və orada əlavə etdiyiniz qovluqları skan edir. Tray menyusundakı **Repo-ları skan et** bəndi skanı dərhal başladır.

- **Axtarış qaydaları:** default olaraq 5 səviyyə dərinliyə qədər axtarır, `settings.json`-dakı `scanMaxDepth` ilə dəyişmək olur. `node_modules`, `bin`, `obj` kimi qovluqlara və repo-ların öz daxilinə girmir.
- **Hər repo üçün göstərilənlər:**
  - hansı profilə aid olduğu (remote alias-a, qovluğa, owner-ə və ya GitHub istifadəçi adına görə);
  - əsas remote;
  - commit-lərin hansı email ilə ediləcəyi.
- **Qırmızı ilə göstərilən problemlər:**
  - HTTPS remote;
  - alias-sız `git@github.com:` remote;
  - qovluğun profilinə uyğun olmayan başqa profilin alias-ı;
  - səhv və ya təyin olunmamış email.
- **Düzəliş:** **Düzəlt** düyməsi dəqiq dəyişikliyi göstərib təsdiq istəyir, sonra `git remote set-url` əmrini işlədir və ya repo daxilindəki `user.email`-i silir. Hər düzəlişdən əvvəl repo-nun `.git/config` faylının backup-ı yaradılır. **Hamısını düzəlt** düyməsi bütün düzəlişləri bir dəfəyə tətbiq edir.
- **Toxunulmayan repo-lar:** yalnız əsas (`origin`) remote yoxlanılır. Fork-ların HTTPS `upstream`-inə və GitHub olmayan remote-lara (TFS, GitLab) toxunulmur.

### Diaqnostika

**Profil üzrə SSH testi.** git-in həqiqətən istifadə etdiyi ssh ilə `ssh -T git@<alias>` işlədir. ssh belə müəyyən edilir: `GIT_SSH_COMMAND` → `core.sshCommand` → `GIT_SSH` → Git-in öz ssh-ı. GitHub-ın salamladığı hesab profilin istifadəçi adı ilə müqayisə olunur. Fərqli hesab çıxırsa, bu key başqa hesaba aiddir.

**Windows yoxlamaları.** Hər birində düzəliş varsa, tətbiq etməzdən əvvəl təsdiq istənilir:

| Yoxlama | Düzəliş |
|---|---|
| `known_hosts`-da GitHub-ın host key-i yoxdur və ya gözlənilməz key var. Key yoxdursa, terminalsız ilk clone "Host key verification failed" xətası ilə dayanır | GitHub-ın key-ləri `api.github.com/meta`-dan götürülür, dərc olunmuş barmaq izləri ilə yoxlanılır və köhnə github.com qeydləri əvəz olunur. Digər host-lar olduğu kimi qalır |
| Private key-i başqaları da oxuya bilir (Windows OpenSSH belə key-i qəbul etmir) | Faylı yalnız siz, SYSTEM və Administrators oxuya biləcək |
| Passphrase-li key var, amma `ssh-agent` işləmir və ya key agent-ə yüklənməyib | Lazım olan administrator əmri kopyalanır, siz onu özünüz işlədirsiniz (GitPersona admin hüququ istəmir) |
| git-in öz ssh-ı Windows `ssh-agent` ilə işləyə bilmir | `core.sshCommand` Windows OpenSSH-a yönəldilir (Ayarlar → SSH, diff baxışından keçir) |
| `HOME` dəyişəni istifadəçi profilindən fərqlidir, və ya `GIT_SSH` / `GIT_SSH_COMMAND` təyin olunub | Yalnız məlumat verilir |
| Credential Manager-də köhnə `git:https://github.com` girişləri var | Yalnız siz istəsəniz silinir. `gh:` girişlərinə toxunulmur |

### Tray

Tray menyusunda bunlar var:

- profillər;
- **URL-dən clone et…**;
- **Repo-ları skan et**;
- **GitHub CLI hesabı** — yalnız `gh` quraşdırılıbsa görünür. Aktiv hesab işarələnir, başqa hesabı seçəndə `gh auth switch` işlədilir;
- **Çıxış**.

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
