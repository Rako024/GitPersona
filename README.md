# GitPersona

GitPersona bir Windows kompüterində bir neçə GitHub hesabını idarə edir. Hər repo üçün düzgün SSH key, düzgün `user.email` və düzgün hesab avtomatik seçilir, siz heç nə xatırlamalı olmursunuz.

Tətbiq admin hüququ istəmir. `~/.ssh/config` və `~/.gitconfig` fayllarında yalnız öz bloklarını dəyişir və hər dəyişiklikdən əvvəl diff göstərib təsdiq istəyir. Arxitektura və qərarlar [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) faylındadır.

## Xüsusiyyətlər

- **Profillər:** hər hesab üçün git identity, SSH key, SSH alias (`github-work`), qovluqlar və GitHub owner-ləri saxlanılır.
- **SSH key-lər:** ed25519 key generasiya edilir və ya mövcud key seçilir. GitHub-a OAuth Device Flow və ya PAT ilə yüklənir. Brauzerdə səhv hesab açıqdırsa, proses dayanır.
- **Avtomatik identity:** `includeIf` qaydaları ilə profil qovluğundakı və profilin alias-ını istifadə edən hər repo düzgün email ilə commit edir.
- **Ağıllı clone:** URL yapışdırılır, profil owner-ə görə seçilir, alias URL-i və qovluq avtomatik hazırlanır.
- **Commit qoruyucusu:** səhv email ilə edilən commit-i bloklayır, repo-ların öz hook-larını isə işlətməyə davam edir.
- **Repo skaneri:** HTTPS və ya alias-sız remote-ları, səhv email-ləri tapır və bir kliklə düzəldir.
- **Diaqnostika:** profil üzrə `ssh -T` testi aparır. Windows-a xas problemləri də yoxlayır: known_hosts, key icazələri, ssh-agent, ssh ziddiyyəti, köhnə Credential Manager girişləri.
- **Tray:** profillər, clone, skan və `gh auth switch` ilə hesab dəyişmə. İstəyə görə tətbiq Windows ilə birlikdə açıla bilər.
- **İnterfeys:** Azərbaycanca və İngiliscə, açıq və tünd mövzu.

## Quraşdırma

**Tələblər:**

- Windows 10 və ya 11 (x64);
- [Git for Windows](https://git-scm.com/download/win);
- istəyə görə [GitHub CLI](https://cli.github.com/) (tray-də hesab dəyişmək üçün).

Son versiyanı [Releases](https://github.com/Rako024/GitPersona/releases) səhifəsindən yükləyin. **İki variant var:**

- **Installer:** `GitPersona-Setup-<versiya>.exe` faylını işə salın. Proqram `%LOCALAPPDATA%\Programs\GitPersona` qovluğuna, yalnız sizin istifadəçiniz üçün quraşdırılır, admin hüququ istəmir.
- **Portativ:** `GitPersona.exe` faylını istənilən qovluğa qoyub işə salın. .NET runtime faylın içindədir.

Hər iki halda tətbiq bağlananda tray-də qalır. Tamamilə çıxmaq üçün tray menyusunda **Çıxış** seçin.

Exe rəqəmsal imza ilə imzalanmayıb. Ona görə ilk açılışda Windows SmartScreen "Windows protected your PC" xəbərdarlığını göstərə bilər. Faylı bu repo-nun Releases səhifəsindən yükləmisinizsə, **More info → Run anyway** seçin. Və ya mənbə kodundan özünüz build edin (aşağıdakı **İnkişaf** bölməsinə baxın).

## İlk addımlar

1. **Profillər → Yeni profil** seçin, ad, `user.name`, `user.email` və GitHub istifadəçi adını daxil edin.
2. **Yeni key yarat…** basın.
3. **GitHub-a yüklə…** basın və brauzerdə GitHub ilə daxil olun. Bunun üçün heç nə qurmaq lazım deyil. İstəsəniz, PAT da istifadə edə bilərsiniz.
4. Profilə **qovluq** və **owner** əlavə edin, **Yadda saxla** basın. Göstərilən diff-i nəzərdən keçirib **Tətbiq et** basın.
5. **Diaqnostika** səhifəsində profilinizi **Yoxla** ilə test edin. Lazım olsa, **GitHub key-lərini əlavə et** ilə known_hosts-u düzəldin.
6. Köhnə repo-lar üçün **Repo skaneri → Skan et** seçin. Yeni repo-lar üçün **Clone** səhifəsindən istifadə edin.
7. İstəyə görə **Ayarlar → Commit qoruyucusu** və **Windows ilə açılsın** seçimlərini aktiv edin.

## İstifadə

### Profillər

- **ID və alias:** göstərilən addan profil ID-si (`is-hesabi`) və SSH alias (`github-is-hesabi`) avtomatik törədilir, onları dəyişə bilərsiniz.
- **Qovluqlar:** bu qovluqlardakı repo-lar profilin identity-si ilə commit edir. Qovluqlar iç-içə ola bilər, belə halda ən dərin qovluğun profili keçərlidir.
- **Remote alias:** remote-u `git@<alias>:owner/repo.git` olan repo, harada yerləşməsindən asılı olmayaraq, profilin email-ini istifadə edir. Alias qaydası qovluq qaydasını üstələyir, çünki push-u həmin alias-ın key-i edir.
- **Owner-lər:** ağıllı clone zamanı profili seçmək üçün istifadə olunur.
- **Yadda saxla:** profili saxlayır. Konfiqurasiya fayllarına isə yalnız diff baxışında **Tətbiq et** basandan sonra yazılır.

### Öz OAuth App-iniz (istəyə bağlı)

"GitHub ilə daxil ol" funksiyası github.com-da GitPersona-nın öz OAuth App-indən istifadə edir, ona görə heç nə qurmaq lazım deyil. Hər hesab tətbiqi brauzerdə ayrıca təsdiqləyir. Token yalnız sizin kompüterinizdə qalır. Device Flow-da client secret işlədilmir, ona görə tətbiqin sahibi bu token-ləri görə bilmir.

Öz OAuth App-inizi iki halda yaratmalısınız: əgər onu istifadə etmək istəyirsinizsə, ya da GitHub Enterprise Server işlədirsinizsə (orada GitPersona-nın tətbiqi yoxdur). Bunun üçün:

1. Ayarlar → GitHub → **OAuth App qeydiyyatı…** düyməsini basın (və ya <https://github.com/settings/applications/new> səhifəsini açın).
2. Sahələri doldurun:
   - **Application name:** `GitPersona`
   - **Homepage URL:** istənilən ünvan, məsələn `https://github.com/<username>`
   - **Authorization callback URL:** `http://127.0.0.1`. Device Flow bu ünvanı istifadə etmir, amma sahəni boş qoymaq olmur.
3. **Register application** basın, açılan səhifədə **Enable Device Flow** seçimini işarələyin və **Update application** basın.
4. **Client ID**-ni kopyalayıb GitPersona → Ayarlar → GitHub → *OAuth App client ID* sahəsinə yapışdırın. **Client secret yaratmayın**, o lazım deyil. Sahəni boşaltsanız, yenidən GitPersona-nın öz tətbiqi istifadə olunur.

### Key-i GitHub-a yükləmək

1. Profildə **GitHub-a yüklə…** → **GitHub ilə daxil ol** seçin.
2. **Kodu kopyala və GitHub-ı aç** basın, kodu brauzerdə daxil edib təsdiqləyin.

   > Brauzerdə **profilin aid olduğu hesabla** daxil olmalısınız. Başqa hesab açıqdırsa, linki gizli (private) pəncərədə açın. GitPersona daxil olunan hesabı profilin GitHub istifadəçi adı ilə müqayisə edir və uyğun deyilsə dayanır.
3. Key hesaba yüklənir. Key hesabda artıq varsa, ikinci dəfə yüklənmir.
4. Hesabın təsdiqlənmiş email-lərindən və ya `noreply` ünvanından birini commit email-i kimi seçin. **Hazırdır** basın, sonra profildə **Yadda saxla** basın.

Brauzer əvəzinə personal access token da istifadə edə bilərsiniz. Lazım olan icazələr:

- **classic token:** `write:public_key` və `user:email` scope-ları;
- **fine-grained token:** *Git SSH keys: Read and write* və *Email addresses: Read-only* icazələri.

Token yalnız Windows Credential Manager-də `GitPersona:github.com:<login>` adı ilə saxlanılır. Onu redaktordakı **Girişi unut** düyməsi ilə silə bilərsiniz.

Təşkilatınız SAML SSO istifadə edirsə, key-i GitHub-da *Settings → SSH and GPG keys → Configure SSO* bölməsində həmin təşkilat üçün ayrıca təsdiqləməlisiniz.

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
- **Öz `core.hooksPath`-ı olan repo-lar** (husky v5+) yoxlanmır, çünki lokal ayar global ayarı üstələyir. Skaner belə repo-ları göstərir.
- **Python pre-commit framework** `core.hooksPath` qurulu olanda `pre-commit install` əmrindən imtina edir. Onu Git Bash-də belə quraşdırın:

  ```bash
  GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=core.hooksPath GIT_CONFIG_VALUE_0= pre-commit install
  ```

  Quraşdırılmış hook GitPersona vasitəsilə işləyəcək.
- **Sürət:** Windows-da qoruyucu hər commit-ə təxminən 0,3 s əlavə edir.
- **Mesajların dili:** qoruyucunun mesajları interfeysin dilindədir. Qoruyucu GitPersona bağlı olanda da işləyir.

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

## GitPersona nəyi dəyişir və harada saxlayır

| Nə | Harada |
|---|---|
| Profillər, ayarlar | `%APPDATA%\GitPersona\profiles.json`, `settings.json` |
| Loglar (14 gün saxlanılır, token-lər yazılmır) | `%APPDATA%\GitPersona\logs\` |
| Commit qoruyucusu hook-ları | `%APPDATA%\GitPersona\hooks\` |
| GitHub token-ləri | Windows Credential Manager → `GitPersona:<host>:<login>` |
| SSH Host blokları | `~/.ssh/config`, faylın əvvəlində GitPersona bloku |
| `includeIf` qaydaları, `core.hooksPath`, `core.sshCommand` | `~/.gitconfig`, faylın sonunda GitPersona bloku |
| Profilin git identity-si | `~/.gitconfig-<profil>` (tamamilə GitPersona-ya məxsusdur) |
| GitHub host key-ləri (yalnız Diaqnostikadan düzəliş edəndə) | `~/.ssh/known_hosts` |
| "Windows ilə açılsın" | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GitPersona` |

GitPersona öz marker blokundan kənardakı məzmuna toxunmur:

```
# >>> GitPersona managed >>>
...
# <<< GitPersona managed <<<
```

Konfiqurasiya fayllarına yazmazdan əvvəl həmişə diff göstərilir. Fayl preview-dan sonra dəyişibsə, yazma dayandırılır. Hər dəyişdirilən faylın yanında `<ad>.yyyyMMdd-HHmmss.bak` backup-ı yaradılır, hər fayl üçün son 20 backup saxlanılır. Private key-lər `~/.ssh`-da qalır və heç yerə köçürülmür.

## Silmək

1. **Ayarlar → GitPersona konfiqurasiyasını sil** seçin. Bu, aşağıdakıları edir (diff baxışından sonra):
   - `~/.ssh/config` və `~/.gitconfig`-dən GitPersona bloklarını çıxarır;
   - `~/.gitconfig-<profil>` fayllarını və commit qoruyucusu hook-larını silir;
   - saxlanılmış GitHub girişlərini silir.

   Profillər və SSH key-lər qalır.
2. **Tətbiqi silin.** Installer ilə quraşdırmısınızsa, Windows → Proqramlar bölməsindən silin. Portativ variantda exe-ni silmək kifayətdir.
3. Lazım olsa, `%APPDATA%\GitPersona` qovluğunu əl ilə silin.

Birinci addımı etmədən tətbiqi silsəniz, yazılmış konfiqurasiya işləməyə davam edir. Commit qoruyucusu da tətbiqsiz işləyir.

## Problemlərin həlli

| Simptom | Səbəb və həll |
|---|---|
| `Host key verification failed` | `known_hosts`-da GitHub-ın key-i yoxdur. Diaqnostika → **GitHub key-lərini əlavə et** |
| `Permission denied (publickey)` | Key hesaba yüklənməyib. Profil → **GitHub-a yüklə…** |
| `ERROR: Repository not found` (repo mövcuddur) | Repo-ya girişi olmayan hesabın key-i istifadə olunur. Clone səhifəsində düzgün profili seçin, ya da Skanerdə remote-u düzəldin |
| `Could not resolve hostname github-<id>` | Alias hələ `~/.ssh/config`-də yoxdur. Profillər → **Konfiqurasiyanı tətbiq et** |
| Commit-lər hələ də köhnə email ilə gedir | Repo-da lokal `user.email` var və ya remote HTTPS-dir. Skaner hər ikisini göstərir və düzəldir |
| Push zamanı passphrase soruşulur | Passphrase-li key-lər üçün Windows ssh-agent və Windows OpenSSH lazımdır. Diaqnostikadakı addımlara baxın |
| `UNPROTECTED PRIVATE KEY FILE` | Key faylını başqaları oxuya bilir. Diaqnostika → **Girişi məhdudlaşdır** |
| HTTPS remote-lar səhv hesabla push edir | Credential Manager-də köhnə `git:https://github.com` girişi var. Repo-ları SSH-a keçirin (Skaner), sonra girişi Diaqnostikadan silin |

## İnkişaf

**Tələblər:** [.NET 10 SDK](https://dotnet.microsoft.com/download) və Git for Windows.

```powershell
dotnet build GitPersona.slnx
dotnet test GitPersona.slnx
dotnet run --project src/GitPersona.App
```

- `--minimized` tətbiqi pəncərəsiz, yalnız tray-də açır. "Windows ilə açılsın" seçimi bu arqumentdən istifadə edir.
- Testlərin bir hissəsi temp qovluqda real Git for Windows, OpenSSH, `ssh-keygen` və Windows Credential Manager ilə işləyir. Credential Manager-də unikal adlı qeydlər yaradılır və test bitəndə silinir. Testlər sizin öz git və ssh konfiqurasiyanızı nə oxuyur, nə də dəyişir.

### Sandbox-da sınaq

Tətbiqi real fayllarınıza toxunmadan sınamaq üçün:

```powershell
$env:GITPERSONA_HOME_DIR = "$env:TEMP\gp-sandbox\home"   # ~/.ssh və ~/.gitconfig əvəzinə
$env:GITPERSONA_DATA_DIR = "$env:TEMP\gp-sandbox\data"   # %APPDATA%\GitPersona əvəzinə
$env:HOME = $env:GITPERSONA_HOME_DIR                      # git də sandbox konfiqurasiyasını oxusun
dotnet run --project src/GitPersona.App
```

### Publish və installer

```powershell
powershell -ExecutionPolicy Bypass -File build/publish.ps1              # testlər + publish\GitPersona.exe
powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Installer   # + installer\Output\GitPersona-Setup-1.0.0.exe
```

Skript spesifikasiyadakı əmri işlədir:

```powershell
dotnet publish src/GitPersona.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Nəticə tək `GitPersona.exe` faylıdır (~62 MB, .NET runtime içindədir). Installer üçün [Inno Setup 6.3+](https://jrsoftware.org/isdl.php) quraşdırılmalıdır, skript `installer/GitPersona.iss` faylındadır.

### Layihənin quruluşu

| Qovluq | Məzmun |
|---|---|
| `src/GitPersona.Core` | UI-dan asılı olmayan məntiq: config planı və yazılması, git/ssh/gh, GitHub API, hook-lar, skaner, diaqnostika |
| `src/GitPersona.App` | WPF tətbiqi (MVVM, WPF-UI, tray, AZ/EN resurslar) |
| `tests/GitPersona.Core.Tests` | Core testləri, o cümlədən real git/ssh ilə inteqrasiya testləri |
| `tests/GitPersona.App.Tests` | ViewModel məntiqinin testləri (məsələn GitHub qoşulma axını) |
| `build/publish.ps1` | Test, publish və installer |
| `installer/GitPersona.iss` | Inno Setup skripti (per-user, admin-siz) |
| `tools/New-AppIcon.ps1` | Tətbiq ikonunu yenidən yaradır |
| `docs/ARCHITECTURE.md` | Arxitektura və qərarlar |

## Lisenziya

[MIT](LICENSE)
