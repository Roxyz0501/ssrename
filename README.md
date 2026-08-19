# スクリーンショットリネーム（SSRename）

作者: `Roxyz0501`

FFXIVで新しいスクリーンショットが作成されたとき、撮影時のキャラクター・ワールド・場所・天気・エオルゼア時刻をファイル名へ付加するACTプラグインです。OverlayPluginは不要です。

## 標準のファイル名

```text
ffxiv_元ファイルの日時_キャラクター名@ワールド名_場所_天気_ETHHmm.拡張子
```

例:

```text
ffxiv_20260731_142530_123_Taro Yamada@Chocobo_リムサ・ロミンサ：上甲板層_快晴_ET1842.png
```

元ファイル名に `YYYYMMDD_HHMMSS_mmm` が含まれている場合、その値をそのまま `{timestamp}` に使います。含まれない場合だけファイルの更新日時を使います。同名ファイルが既にある場合は `_2`、`_3` のように連番を付けます。

## インストール

1. `dist/SSRename.dll` を任意のACTプラグイン用フォルダへ置きます。
2. ACTの `Plugins` → `Plugin Listing` → `Browse...` で `SSRename.dll` を選び、`Add/Enable Plugin` を押します。
3. 同じフォルダに `SSRename.Updater.exe` も配置します。
4. `Screenshot Rename` タブでFFXIV接続と監視状態を確認します。

FFXIV Parsing Plugin (`FFXIV_ACT_Plugin.dll`) を先に有効にしてください。キャラクター名、ワールド名、テリトリーID、サーバー時刻は同プラグインの公開APIから取得します。

## 設定

- 監視先の初期値: `%USERPROFILE%\Documents\My Games\FINAL FANTASY XIV - A Realm Reborn\screenshots`
- 保存先の初期値: 監視先と同じフォルダ
- 保存先は別フォルダへ変更可能（画像をそのフォルダへリネーム移動）
- テンプレートで使える項目: `{character}`、`{world}`、`{place}`、`{timestamp}`、`{weather}`、`{et}`
- 設定ファイル: `%APPDATA%\Advanced Combat Tracker\Config\SSRename.config.xml`
- 言語: English / 日本語 / 简体中文 / 한국어
- 起動時の更新確認: 既定ON

設定画面の「状態・履歴」タブには、現在取得している情報と直近50件のリネーム履歴を表示します。キャラクター名またはワールド名を取得できない場合は誤った名前を付けず、元画像をそのまま残します。

### 言語の決定

旧Configを含め、`Language` が存在しない初回起動だけWindowsのUIカルチャーを参照します。`ja` 系は日本語、`zh` 系は簡体字中国語、`ko` 系は韓国語、それ以外は英語になります。選択結果は直ちにConfigへ保存され、以後OS言語で上書きされません。

設定画面の言語欄からいつでも変更でき、UIへ即時反映されます。未知の言語コードと不足した翻訳キーは英語へフォールバックします。

## 任意支援

設定画面の「支援」タブから、[Ko-fiでRoxyz0501を支援](https://ko-fi.com/roxyz0501)できます。タブ名に星などの記号は使用していません。

支援は完全に任意です。支援しなくてもSSRenameのすべての機能を利用でき、機能差はありません。リンクは支援タブのボタンを明示的にクリックした場合だけ既定のブラウザで開きます。起動時のポップアップ、自動遷移、繰り返し通知、機能制限はありません。

## 更新機能

設定画面の「更新」タブでは、起動時の更新確認をON/OFFでき、「今すぐ確認」も実行できます。確認はUIを止めない非同期処理で、安定版ReleaseのみをSemVer比較します。更新がある場合は現在版、最新版、更新内容、「更新する」「後で」をACT画面内に表示します。ダウンロードは「更新する」を押した場合だけ行われ、自動更新はしません。

更新パッケージは次を検証します。

- GitHubのHTTPS API／Release assetのみ許可
- draft／prereleaseを除外
- `SSRename-<version>.zip` とReleaseタグ、DLL名、DLLバージョンの一致
- `SHA256SUMS.txt` がある場合のSHA-256照合
- ZIP内の許可ファイル限定、絶対パス／親ディレクトリ／サブディレクトリ拒否
- 最大サイズとリダイレクト先GitHubホストの制限

検証済みDLLは一時領域へ置き、`SSRename.Updater.exe` がACT終了を待ちます。終了後、現行DLLを `SSRename.dll.bak` へバックアップしてから置換し、失敗時はバックアップを復元します。更新後はACTを再起動してください。

### 更新元

安定版の更新確認と配布物の取得元は、公開リポジトリ [Roxyz0501/ssrename](https://github.com/Roxyz0501/ssrename) のGitHub Releasesです。認証トークンをプラグインへ埋め込まず、公開APIとHTTPSのRelease assetだけを使用します。

### GitHub Releaseの作成

1. `AssemblyInfo.cs` のAssemblyVersion／AssemblyFileVersionをRelease版へ更新します。
2. ACTを参照できるWindows環境で次を実行します。

```powershell
.\tools\New-ReleasePackage.ps1 -Version 1.2.3
```

3. タグを `v1.2.3` のSemVer形式で作成します。
4. 安定版GitHub Releaseへ `release/SSRename-1.2.3.zip` と `release/SHA256SUMS.txt` を添付します。
5. draft／prereleaseは通常の自動確認対象になりません。GitHubトークンはプラグインへ埋め込まないでください。

`vX.Y.Z` タグをpushした場合、`.github/workflows/build-release.yml` がWindows上でビルドとテストを行い、成功時だけ同じ形式のZIPとSHA-256マニフェストを安定版Releaseへ添付します。ワークフローは公式ACT配布ページからビルド用参照を取得し、Releaseジョブ以外にはリポジトリ書き込み権限を与えません。

## ビルドとテスト

Visual Studio 2022または.NET Framework 4.8をビルドできるMSBuild環境で実行します。

```powershell
dotnet msbuild SSRename.sln -t:Rebuild -p:Configuration=Release
.\tests\SSRename.Tests\bin\Release\SSRename.Tests.exe
```

成果物は `dist/SSRename.dll` です。

## ゲームデータについて

場所名と天気は、ビルド時にDLLへ組み込んだ日本語ゲームデータとFFXIVの天気決定式からオフラインで求めます。新しいパッチで追加されたエリアへ対応する際は、`src/SSRename/Data` のCSV更新と再ビルドが必要です。データの出典は `THIRD_PARTY_DATA.md` を参照してください。

## ライセンス

現時点ではライセンスは未設定です。`LICENSE` ファイルは追加しておらず、再利用・再配布条件はまだ明示していません。
