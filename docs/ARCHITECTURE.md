# GitPersona — Arxitektura

## Qərarlar

| Mövzu | Qərar |
|---|---|
| Stack | C# / **.NET 10 LTS** + WPF, MVVM (CommunityToolkit.Mvvm). .NET 8-in dəstəyi 10.11.2026-da bitir. |
| UI | WPF-UI (Fluent görünüş, light/dark), H.NotifyIcon.Wpf (tray) |
| Çıxış | self-contained single-file `GitPersona.exe` (win-x64), admin tələb etmir |
| GitHub auth | OAuth Device Flow (OAuth App-i istifadəçi yaradır, `client_id` Settings-də daxil edilir). PAT ehtiyat variantdır |
| SSH key | ed25519, default olaraq passphrase-siz, istəyə görə passphrase |
| Uyğunsuz repo | Heç bir profilə uyğun gəlməyən repo-ya qarışılmır, orada global identity işləyir |

## Qovluq strukturu

```
GitPersona.slnx
Directory.Build.props            # net10.0-windows, Nullable, ortaq ayarlar
global.json                      # .NET 10 SDK
src/
  GitPersona.Core/               # UI-dan asılı olmayan bütün məntiq (unit test olunur)
    Models/                      # Profile, AppSettings, ...
    Infrastructure/              # ProcessRunner, AppPaths/UserPaths, AtomicFileWriter, BackupManager, JsonFileStore
    ConfigFiles/                 # ManagedBlock, ConfigChange, ConfigChangeSet, ConfigWriter
    Profiles/                    # ProfileStore, ProfileResolver (owner / qovluq / alias → profil)
    Git/                         # GitUrlParser, GitService
    Ssh/                         # SshConfigService, SshKeyService, KnownHostsService
    GitConfig/                   # GitConfigService (includeIf + profil faylları)
    GitHub/                      # GitHubApiService (Device Flow, /user, /user/emails, /user/keys)
    Security/                    # CredentialStore (Windows Credential Manager, P/Invoke)
    Hooks/                       # HookService + dispatch.sh (embedded resource)
    Scanning/                    # RepoScanner
    Diagnostics/                 # DiagnosticsService
  GitPersona.App/                # WPF
    Views/, ViewModels/          # Profiles, Clone, Scanner, Diagnostics, Settings, DiffPreview
    Tray/                        # TrayService
    Platform/                    # StartupService (HKCU Run), ThemeService, SingleInstance
    Localization/                # Strings.resx (en), Strings.az.resx, {l:Loc} markup extension
tests/
  GitPersona.Core.Tests/         # xUnit: fayl yazma, parser-lər, hook inteqrasiyası (temp qovluqda real git)
installer/GitPersona.iss         # Inno Setup, per-user, admin-siz
docs/
```

## Data

- `%APPDATA%\GitPersona\profiles.json` saxlayır: profillər.
- `%APPDATA%\GitPersona\settings.json` saxlayır: dil, mövzu, commit guard, OAuth `client_id`, skan dərinliyi.
- `%APPDATA%\GitPersona\logs\` saxlayır: gündəlik log faylları (14 gün).
- Profilin `id`-si (slug, məs. `work`) bütün adları yaradır:
  - alias: `github-work`
  - git config faylı: `~/.gitconfig-work`
  - SSH key: `~/.ssh/id_ed25519_work`
- Token-lər yalnız Windows Credential Manager-də saxlanılır (`GitPersona:github.com:<login>` adı ilə), JSON-a heç vaxt yazılmır.
- `host` sahəsinin default dəyəri `github.com`-dur. Dəyişdirilə bildiyi üçün GitHub Enterprise də dəstəklənir.

## Konfiqurasiya yazılması

- Bütün yazmalar eyni yoldan keçir:
  1. `ConfigChangeSet` yaranır (fayl, cari məzmun, yeni məzmun).
  2. Diff preview göstərilir və istifadəçi təsdiq edir.
  3. Fayl preview-dan sonra dəyişibsə, yazma dayandırılır.
  4. Backup yaradılır.
  5. Fayl atomik yazılır: temp fayl + `File.Replace`, ACL qorunur.
- Backup faylın yanında `<ad>.yyyyMMdd-HHmmss.bak` adı ilə yaradılır. Hər fayl üçün tətbiqin yaratdığı son 20 backup saxlanılır.
- Marker bloku xaricindəki məzmun dəyişmir: BOM və sətir sonları qorunur. `Remove(Upsert(x)) == x` şərti testlərlə yoxlanılır.
- `~/.ssh/config`: blok faylın **əvvəlinə** yazılır, çünki ssh-da ilk uyğun gələn dəyər qalib gəlir.
- `~/.gitconfig`: blok faylın **sonuna** yazılır, çünki git-də sonuncu dəyər qalib gəlir. Belə olanda `includeIf` global `user.email`-i override edir.
- Hər profil üçün iki `includeIf` qaydası yazılır:
  - `[includeIf "gitdir/i:C:/work/"]` — qovluğa görə işləyir.
  - `[includeIf "hasconfig:remote.*.url:git@github-work:*/**"]` — remote alias-a görə işləyir. Burada `:*/**` lazımdır, çünki git-də `**` yalnız `/`-dən sonra rekursivdir.

  Hər ikisi uyğun gəlsə, alias qaydası qalib gəlir.
- `~/.gitconfig-<id>` faylı tam tətbiqə məxsusdur. İçində `user.name`, `user.email` və `credential.https://github.com.username` olur.
- `core.hooksPath` (guard aktiv olanda) və lazım olsa `core.sshCommand` da eyni blokda yazılır.

## Commit qoruyucusu

- Global `core.hooksPath` lokal hook-ların **hamısını** söndürür. Ona görə `%APPDATA%\GitPersona\hooks\` qovluğunda client-side hook-lar üçün stub olur.
  - Stub qoyulmayan hook-lar: tez-tez çağırılan `reference-transaction` və `post-index-change`, server-side hook-lar və p4 hook-ları.
  - Səbəb: Windows-da hər stub hər çağırışda bir `sh` başladır. 27 stub ilə commit 3,8 s çəkirdi, indiki 15 stub ilə ~0,45 s çəkir (hook-suz ~0,12 s).
- Hər stub ortaq `dispatch.sh`-ı source edir. Ikinci `sh` açılmır, adi yol yalnız builtin-lərlə işləyir. Dispatcher:
  1. `pre-commit` üçün identity yoxlanılır.
  2. `$(git rev-parse --git-common-dir)/hooks/<ad>` faylı mövcuddursa, eyni arqumentlər və stdin ilə `exec` edilir.
  3. Guard aktiv edilməzdən əvvəl global `hooksPath` varıdısa, ona da ötürülür.
- Yoxlama yalnız `sh` və tətbiqin yaratdığı `rules.tsv` ilə işləyir, ona görə tətbiq bağlı olanda da işləyir. Bir `git config` və bir `awk` çağırışı ilə git-in müəllif email prioritetini tətbiq edir: `GIT_AUTHOR_EMAIL` → `author.email` → `user.email` → `EMAIL`.
- Qovluqlar `rules.tsv`-də uzun yol formasında saxlanılır (`GetLongPathName`), çünki Git Bash-in `pwd -W` əmri 8.3 qısa adları açır.
- Guard söndürüləndə `~/.gitconfig` hələ `core.hooksPath`-a istinad edirsə, stub-lar yoxlamasız saxlanılır. Belə olanda repo-lar öz hook-larını itirmir. İstinad silinəndən sonra fayllar da silinir.
- Git LFS: repo-da öz LFS hook-u yoxdursa, `pre-push`, `post-checkout`, `post-commit` və `post-merge` hook-ları `git lfs <hook>` əmrini çağırır.
- Gözlənilən profil remote alias-ın profilidir, alias yoxdursa qovluğun profili götürülür:
  - effektiv `user.email` profilin email-inə uyğun deyilsə, commit bloklanır;
  - profil tapılmırsa, commit keçir.
- Husky istifadə edən repo-larda lokal `core.hooksPath` global ayarı üstələyir və guard orada işləmir. Skaner bunu göstərir.
- `pre-commit` (Python) `core.hooksPath` qurulu olanda install etməkdən imtina edir. Bunun yolu README-də izah olunacaq.

## Xarici proseslər

- `ProcessRunner` arqumentləri shell-siz `ArgumentList` ilə ötürür. Timeout və ləğv olanda bütün proses ağacını dayandırır. UTF-8 işlədir, log-da həssas dəyərləri maskalayır.
- Tətbiqin işə saldığı git/ssh əmrləri `GIT_TERMINAL_PROMPT=0` və `BatchMode=yes` ilə işləyir. Belə olanda parol sorğusu gəlsə, proses gözləyib qalmır.
- `ssh -T` testi git-in həqiqətən istifadə etdiyi ssh ilə icra olunur. Cavabdakı `Hi <login>!` profilin username-i ilə müqayisə edilir.
- `known_hosts`-da github.com yoxdursa, GitHub-ın rəsmi host key-ləri əlavə edilir.

## Test

- Servislər `UserPaths`/`AppPaths` abstraksiyası ilə işləyir. Testlər yalnız temp qovluqlarda icra olunur.
- Real `~/.ssh/config` və `~/.gitconfig` fayllarını yalnız istifadəçi UI-dan, preview-dan sonra dəyişir.

## MVP mərhələləri

| # | Mərhələ | Məzmun |
|---|---|---|
| 1 | Skelet | Solution, Core infrastrukturu, testlər. WPF shell: naviqasiya, light/dark, AZ/EN, tray, single-instance, "Windows ilə açılsın" |
| 2 | Profillər | Profillər UI, SSH key (generasiya/seçim), ssh/git config yazılması (diff preview ilə) |
| 3 | GitHub | Device Flow, PAT, key upload, Credential Manager |
| 4 | Clone | URL parser, owner qaydaları, progress |
| 5 | Commit guard | Dispatcher, chain, toggle, real git ilə inteqrasiya testləri |
| 6 | Skaner | Repo skaneri və "Düzəlt" düyməsi |
| 7 | Tray + Diaqnostika | `gh auth switch`, ssh-agent, ssh ziddiyyəti, köhnə Credential Manager girişləri |
| 8 | Paylama | Single-file publish, Inno Setup, README, "GitPersona konfiqurasiyasını sil" funksiyası |
