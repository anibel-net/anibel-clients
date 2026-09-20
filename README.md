# Anibel.Net

Натыўныя праграмы для [anibel.net](https://anibel.net): анімэ, мангі, кіно, гульняў і кніг. Агульнае ядро на Rust забяспечвае працу з GraphQL API сайта, а кожная праграма выкарыстоўвае ўласны інтэрфейс сваёй платформы.

Праект актыўна распрацоўваецца. Windows і Android ужо маюць працоўныя праграмы; iOS і macOS пакуль не рэалізаваныя.

## Магчымасці

- Каталогі, пошук, фільтры, рэкамендацыі і старонкі твораў.
- Уваход ва ўліковы запіс, профіль, закладкі, спісы, ацэнкі і каментарыі.
- Прагляд відэа з выбарам якасці, аўдыядарожкі і субцітраў.
- Чытанне мангі і кніг, захаванне прагрэсу.
- Спампоўкі і лакальная бібліятэка.
- Беларускамоўны інтэрфейс.

У Windows працуюць рэжым «карцінка ў карцінцы», субцітры ASS праз libass і захаванне відэа ў MKV з аўдыядарожкамі, субцітрамі і шрыфтамі адной крыніцы. Відэа і аўдыя капіююцца без перакадавання. Убудаваныя прайгравальнікі Google Drive выкарыстоўваюць WebView2 і не падтрымліваюць такі экспарт.

Android мае інтэрфейсы для тэлефонаў, планшэтаў, Android TV і Google TV. Для відэа выкарыстоўваюцца Media3 і libass, для інтэрфейсу — Jetpack Compose.

## Усталяванне і абнаўленні

Апублікаваныя зборкі будуць даступныя ў [выпусках праекта](https://github.com/anibel-net/anibel-clients/releases). Пакуль выпуск не апублікаваны, праграму можна сабраць з зыходнага кода.

Для Windows усталёўнік наладжвае праверку і спампоўванне абнаўленняў. Пасля спампоўвання праграма прапануе перазапуск. Звычайны ZIP-архіў прызначаны для ручнога абнаўлення: распакуйце яго цалкам і запусціце `Anibel.Net.exe`.

Зборкі Windows пакуль не маюць лічбавага подпісу. Сістэма можа паказаць папярэджанне пра невядомага выдаўца. Спампоўвайце зборкі толькі са старонкі выпуску гэтага рэпазіторыя.

Падтрымка Windows 10 яшчэ патрабуе праверкі на прыладах. Прайгравальнік спачатку выкарыстоўвае сістэмныя кодэкі. Калі Windows не можа дэкадаваць відэа, напрыклад 10-бітны H.264, праграма пераходзіць на праграмнае дэкадаванне FFmpeg. Для ўбудаваных вэб-прайгравальнікаў патрэбны WebView2 Runtime. Мінімальная версія Android — 8.0.

## Структура рэпазіторыя

| Шлях | Прызначэнне |
|---|---|
| `crates/anibel-core/` | Агульны стан праграмы, каманды, захоўванне даных, спампоўкі і C ABI |
| `crates/anibel-domain/` | Мадэлі даных і памылкі |
| `crates/anibel-api/` | Запыты GraphQL і пераўтварэнне адказаў |
| `crates/anibel-player/` | Атрыманне відэакрыніц і звестак пра дарожкі |
| `apps/windows/` | Праграма для Windows на WinUI 3 і .NET 10 |
| `apps/android/` | Праграма для Android на Kotlin і Jetpack Compose |
| `scripts/` | Падрыхтоўка залежнасцей, зборка і ўпакоўка |
| `tools/` | Сродкі праверкі інтэрфейсу, прайгравання і абнаўленняў |
| `docs/` | Тэхнічная дакументацыя |

Правілы працы з данымі знаходзяцца ў Rust. Натыўныя праграмы адказваюць за інтэрфейс, прайграванне, бяспечнае захоўванне ўліковых даных і інтэграцыю з аперацыйнай сістэмай.

## Зборка для Windows

Патрэбныя Windows, PowerShell 7, .NET 10 SDK, Rust праз rustup, інструменты C++ x64 з Visual Studio і Windows SDK. Версія Rust зададзеная ў `rust-toolchain.toml`.

Для натыўных залежнасцей усталюйце MSYS2 у `C:\msys64`. У тэрмінале UCRT64 выканайце:

```sh
pacman -S --needed make curl tar diffutils mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-nasm mingw-w64-ucrt-x86_64-pkgconf mingw-w64-ucrt-x86_64-libxml2 mingw-w64-ucrt-x86_64-zlib mingw-w64-ucrt-x86_64-libass
```

З кораня рэпазіторыя ў PowerShell:

```powershell
./scripts/build-ffmpeg-small.ps1
C:/msys64/usr/bin/bash.exe scripts/fetch-libass.sh
./scripts/build-core.ps1
./scripts/build-app.ps1 -Configuration Debug -Run
```

Для ZIP-архіва з праграмай і залежнасцямі:

```powershell
./scripts/publish-windows.ps1
```

Вынік: `artifacts/share/Anibel-Windows-x64.zip` і файл кантрольнай сумы SHA-256. Усе DLL, рэсурсы і ліцэнзіі трэба захоўваць разам з праграмай.

Для ўсталёўніка і пакетаў абнаўлення:

```powershell
./scripts/publish-windows.ps1 -Installer -UpdateRepository https://github.com/anibel-net/anibel-clients
```

Версія лакальнай зборкі зададзеная ў `apps/windows/version.props`. Тэг `windows-v0.2.0` стварае чарнавік стабільнага выпуску, а `windows-v0.2.1-beta.1` — тэставага. Звычайны запіс у `main` не публікуе абнаўленне. Падрабязнасці ёсць у [інструкцыі выпускаў Windows](apps/windows/RELEASING.md).

## Зборка для Android

Патрэбныя Android Studio або Android SDK, JDK 17, NDK `29.0.14206865`, Rust і `cargo-ndk`. SDK павінен утрымліваць платформу, зададзеную ў `apps/android/app/build.gradle.kts`. Задайце `ANDROID_HOME` або шлях `sdk.dir` у лакальным `apps/android/local.properties`; гэты файл не трапляе ў Git.

```powershell
rustup target add aarch64-linux-android x86_64-linux-android
cargo install cargo-ndk --locked
cd apps/android
./gradlew.bat :app:assembleDebug :app:lintDebug
```

Gradle збірае агульнае ядро для ARM64 і x86_64. APK з'явіцца ў `apps/android/app/build/outputs/apk/debug/`. Іншыя каманды і абмежаванні тэстаў апісаныя ў [дакументацыі Android](apps/android/README.md).

## Праверкі

З кораня рэпазіторыя:

```powershell
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
dotnet test apps/windows/tests/Anibel.App.Tests.csproj -p:Platform=x64
```

Праверкі жывога API запускаюцца асобна і патрабуюць доступу да інтэрнэту:

```powershell
cargo test -p anibel-core --test live -- --ignored
```

[Праверкі прайгравання](tools/PlaybackSmoke/README.md) ахопліваюць MP4, HLS, DASH, асобнае аўдыя, MKV, субцітры, перамотку і перанос паміж вокнамі. Праверка пакетаў абнаўленняў не замяняе поўную праверку ўсталявання і перазапуску праграмы.

## Удзел у распрацоўцы

Перад зменамі прачытайце `AGENTS.md` у корані і ў адпаведным праекце. У паведамленні пра памылку пазначце версію праграмы, аперацыйную сістэму і крокі для паўтарэння. Не дадавайце паролі, токены або асабістыя даныя ў паведамленні і журналы.

У запыце на зліццё апішыце змены і праверкі. Для змен інтэрфейсу карысныя здымкі экрана. Не дадавайце ў Git зборкі, лакальныя налады або спампаваныя медыяфайлы.

## Дакументацыя

- [Агульнае ядро і пратакол](docs/SHARED-CORE-SPEC.md).
- [Размеркаванне адказнасці ў Windows](apps/windows/ARCHITECTURE.md).
- [Выпускі і абнаўленні Windows](apps/windows/RELEASING.md).
- [Зборка і праверкі Android](apps/android/README.md).

## Ліцэнзія

Код праекта распаўсюджваецца паводле [ліцэнзіі MIT](LICENSE). Староннія бібліятэкі захоўваюць уласныя ліцэнзіі. Ліцэнзія праекта не распаўсюджваецца на медыяфайлы, якія прадастаўляе сайт.
