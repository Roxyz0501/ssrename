using System;
using System.Collections.Generic;
using System.Globalization;

namespace SSRename
{
    internal static class Localization
    {
        public const string English = "en";
        public const string Japanese = "ja";
        public const string ChineseSimplified = "zh-CN";
        public const string Korean = "ko";

        private static readonly IDictionary<string, IDictionary<string, string>> Resources =
            new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { English, EnglishResources() },
                { Japanese, JapaneseResources() },
                { ChineseSimplified, ChineseResources() },
                { Korean, KoreanResources() }
            };

        public static string Normalize(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return English;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return Japanese;
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ChineseSimplified;
            if (language.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return Korean;
            return string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : English;
        }

        public static string FromCulture(CultureInfo culture)
        {
            return Normalize(culture == null ? null : culture.Name);
        }

        public static string DetectOsLanguage()
        {
            return FromCulture(CultureInfo.CurrentUICulture);
        }

        public static string T(string language, string key, params object[] args)
        {
            IDictionary<string, string> selected;
            IDictionary<string, string> english = Resources[English];
            string value;
            if (!Resources.TryGetValue(Normalize(language), out selected) ||
                !selected.TryGetValue(key, out value))
                if (!english.TryGetValue(key, out value)) value = key;
            if (args == null || args.Length == 0) return value;
            try { return string.Format(CultureInfo.CurrentCulture, value, args); }
            catch (FormatException) { return value; }
        }

        public static string LanguageDisplayName(string uiLanguage, string value)
        {
            return T(uiLanguage, "language_" + Normalize(value).Replace("-", "_").ToLowerInvariant());
        }

        private static Dictionary<string, string> CommonEnglish()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"title","Screenshot Rename"},{"subtitle","Automatically adds capture information to FFXIV screenshots"},
                {"tab_basic","Settings"},{"tab_filename","File name"},{"tab_status","Status & history"},{"tab_updates","Updates"},{"tab_support","Support"},
                {"language","Language"},{"language_en","English"},{"language_ja","Japanese"},{"language_zh_cn","Simplified Chinese"},{"language_ko","Korean"},
                {"enable_rename","Enable automatic screenshot renaming"},{"watch_folder","Screenshot folder to watch"},{"browse","Browse..."},
                {"destination","Destination"},{"same_destination","Save in the watched folder (default)"},{"destination_changed","Destination folder (when changed)"},
                {"folder_actions","Folder actions"},{"open_destination","Open current destination"},{"basic_note","Images are renamed and moved, not copied. Existing images are not processed; only newly created images are watched."},
                {"template","File name template"},{"template_note","Available: {character}  {world}  {place}  {timestamp}  {weather}  {et}\r\n{timestamp} is taken from the original FFXIV file name; the extension is preserved."},
                {"preview","Preview"},{"template_actions","Template actions"},{"reset_template","Reset to default template"},
                {"connection","FFXIV connection"},{"character","Character"},{"place","Location"},{"weather","Weather"},{"et","Eorzea time"},{"file_watch","File watcher"},{"history","Rename history"},
                {"waiting_connection","Waiting for connection"},{"connected","Connected"},{"initializing","Initializing"},
                {"folder_dialog","Select a folder"},{"destination_missing","The destination folder was not found."},
                {"support_heading","Support Roxyz0501's development"},{"support_intro","If you enjoy SSRename, you can optionally support development through Ko-fi."},
                {"support_voluntary","Support is completely optional. All features are available without support, with no functional differences."},
                {"support_button","Support Roxyz0501 on Ko-fi"},{"support_click_only","Ko-fi opens in your default browser only when you click the button."},
                {"support_opened","Ko-fi was opened in your default browser."},{"support_failed","Could not open Ko-fi: {0}"},
                {"support_launcher_unavailable","The browser launcher is unavailable."},{"support_unsafe_url","The support link was rejected as unsafe."},
                {"updates_heading","SSRename updates"},{"check_startup","Check for updates at startup"},{"check_now","Check now"},{"checking","Checking for updates..."},
                {"current_version","Current version"},{"latest_version","Latest version"},{"last_checked","Last checked"},{"release_notes","What's new"},
                {"update_available","A new version is available."},{"update_none","You are using the latest version."},{"update_button","Update"},{"later_button","Later"},
                {"update_source_missing","Update source is not configured. The GitHub repository must be set by the developer."},
                {"update_check_failed","Update check failed: {0}"},{"update_download","Downloading and validating update..."},
                {"update_ready","Update is ready. Close ACT to install it, then restart ACT."},{"update_ready_manual","The verified update is staged at:\r\n{0}\r\nClose ACT, replace SSRename.dll manually, then restart ACT."},{"update_failed","Update preparation failed: {0}"},
                {"update_skipped","Version {0} will be skipped. You can check again manually at any time."},
                {"watch_paused","File watching is paused."},{"watch_folder_missing","The watched folder was not found."},{"destination_required","Select a destination folder."},
                {"watching","Watching: {0}"},{"watch_start_failed","Could not start watching: {0}"},{"rename_done","Renamed: {0}"},
                {"rename_failed","Failed: {0} — {1}"},{"write_timeout","Could not confirm that the image finished writing."},
                {"context_missing","Character or world information is unavailable, so the original image was kept."},{"watch_error","File watcher error: {0}"},{"watch_queue_full","Screenshot queue is full. The original image was left untouched."},
                {"ffxiv_plugin_missing","FFXIV ACT Plugin is not enabled."},{"waiting_character","Waiting for character information."},
                {"started","Started"},{"stopped","Stopped"},{"settings_save_failed","Could not save settings: {0}"}
                ,{"unknown_character","Unknown character"},{"unknown_world","Unknown world"},{"unknown_place","Unknown location"},{"unknown_time","Unknown time"},{"unknown_weather","Unknown weather"},{"no_weather","No weather"},{"unknown_et","Unknown ET"},{"collision_limit","Too many files have the same name."},{"data_missing","Embedded game data is missing: {0}"},{"ffxiv_read_failed","Could not read FFXIV data: {0}"}
            };
        }

        private static IDictionary<string, string> EnglishResources() { return CommonEnglish(); }

        private static IDictionary<string, string> JapaneseResources()
        {
            var r = CommonEnglish(); Add(r, new[] {
                "subtitle|FFXIVのスクリーンショットへ撮影時の情報を自動で付加します","tab_basic|基本設定","tab_filename|ファイル名","tab_status|状態・履歴","tab_updates|更新","tab_support|支援",
                "language|言語","language_en|英語","language_ja|日本語","language_zh_cn|簡体字中国語","language_ko|韓国語",
                "enable_rename|スクリーンショットの自動リネームを有効にする","watch_folder|監視するスクリーンショットフォルダ","browse|参照…","destination|保存先","same_destination|監視フォルダと同じ場所へ保存する（デフォルト）","destination_changed|保存先フォルダ（変更時）","folder_actions|フォルダ操作","open_destination|現在の保存先を開く",
                "basic_note|画像はコピーではなく、指定した保存先へリネーム移動します。既存画像は処理せず、新しく作成された画像だけを監視します。","template|ファイル名テンプレート","template_note|使用可能: {character}  {world}  {place}  {timestamp}  {weather}  {et}\r\n{timestamp} は元のFFXIVファイル名から取得し、拡張子は自動で保持します。","preview|プレビュー","template_actions|テンプレート操作","reset_template|標準テンプレートへ戻す",
                "connection|FFXIV接続","character|キャラクター","place|場所","weather|天気","et|エオルゼア時刻","file_watch|ファイル監視","history|リネーム履歴","waiting_connection|接続待機中","connected|接続済み","initializing|初期化中","folder_dialog|フォルダを選択してください","destination_missing|保存先フォルダが見つかりません。",
                "support_heading|Roxyz0501の開発を支援","support_intro|SSRenameを気に入っていただけた場合は、Ko-fiから開発を任意で支援できます。","support_voluntary|支援は完全に任意です。支援しなくてもすべての機能を利用でき、機能差はありません。","support_button|Ko-fiでRoxyz0501を支援する","support_click_only|ボタンをクリックした場合のみ、既定のブラウザでKo-fiを開きます。","support_opened|既定のブラウザでKo-fiを開きました。","support_failed|Ko-fiを開けませんでした: {0}",
                "support_launcher_unavailable|ブラウザ起動処理を利用できません。","support_unsafe_url|安全でないリンクのため開けませんでした。",
                "updates_heading|SSRenameの更新","check_startup|起動時に更新を確認する","check_now|今すぐ確認","checking|更新を確認しています…","current_version|現在のバージョン","latest_version|最新バージョン","last_checked|最終確認","release_notes|更新内容","update_available|新しいバージョンがあります。","update_none|最新バージョンを使用しています。","update_button|更新する","later_button|後で","update_source_missing|更新元が未設定です。開発者によるGitHubリポジトリの設定が必要です。","update_check_failed|更新確認に失敗しました: {0}","update_download|更新をダウンロードして検証しています…","update_ready|更新準備ができました。ACTを終了すると適用されます。その後ACTを再起動してください。","update_ready_manual|検証済みの更新DLLを次の場所へ用意しました:\r\n{0}\r\nACTを終了した後、SSRename.dllを手動で置き換えてACTを再起動してください。","update_failed|更新準備に失敗しました: {0}","update_skipped|バージョン {0} をスキップします。今すぐ確認から再確認できます。",
                "watch_paused|監視は一時停止中です。","watch_folder_missing|監視フォルダが見つかりません。","destination_required|保存先フォルダを指定してください。","watching|監視中: {0}","watch_start_failed|監視を開始できません: {0}","rename_done|リネーム完了: {0}","rename_failed|処理失敗: {0} — {1}","write_timeout|画像ファイルの書き込み完了を確認できませんでした。","context_missing|キャラクター名またはワールド名を取得できないため、元ファイルを保持しました。","watch_error|ファイル監視エラー: {0}","watch_queue_full|処理キューがいっぱいのため、画像は移動せず元の場所に残しました。","ffxiv_plugin_missing|FFXIV ACT Pluginが有効ではありません。","waiting_character|キャラクター情報を待っています。","started|起動済み","stopped|停止","settings_save_failed|設定保存失敗: {0}"
                ,"unknown_character|キャラクター不明","unknown_world|ワールド不明","unknown_place|場所不明","unknown_time|時刻不明","unknown_weather|天気不明","no_weather|天気なし","unknown_et|ET不明","collision_limit|同名ファイルの連番上限に達しました。","data_missing|組み込みゲームデータが見つかりません: {0}","ffxiv_read_failed|FFXIVデータを取得できませんでした: {0}"
            }); return r;
        }

        private static IDictionary<string, string> ChineseResources()
        {
            var r = CommonEnglish(); Add(r, new[] {
                "subtitle|自动为FFXIV截图添加拍摄信息","tab_basic|基本设置","tab_filename|文件名","tab_status|状态与历史","tab_updates|更新","tab_support|支持","language|语言","language_en|英语","language_ja|日语","language_zh_cn|简体中文","language_ko|韩语",
                "enable_rename|启用截图自动重命名","watch_folder|监视的截图文件夹","browse|浏览…","destination|保存位置","same_destination|保存到监视文件夹（默认）","destination_changed|自定义保存文件夹","folder_actions|文件夹操作","open_destination|打开当前保存位置","basic_note|图片将被重命名并移动，而不是复制。只监视新建图片，不处理现有图片。","template|文件名模板","template_note|可用项: {character}  {world}  {place}  {timestamp}  {weather}  {et}\r\n{timestamp} 取自FFXIV原文件名，并保留扩展名。","preview|预览","template_actions|模板操作","reset_template|恢复默认模板",
                "connection|FFXIV连接","character|角色","place|地点","weather|天气","et|艾欧泽亚时间","file_watch|文件监视","history|重命名历史","waiting_connection|等待连接","connected|已连接","initializing|正在初始化","folder_dialog|请选择文件夹","destination_missing|找不到保存文件夹。",
                "support_heading|支持Roxyz0501的开发","support_intro|如果您喜欢SSRename，可以通过Ko-fi自愿支持开发。","support_voluntary|支持完全自愿。即使不支持也能使用全部功能，功能没有任何差异。","support_button|在Ko-fi支持Roxyz0501","support_click_only|只有明确点击按钮时才会在默认浏览器中打开Ko-fi。","support_opened|已在默认浏览器中打开Ko-fi。","support_failed|无法打开Ko-fi: {0}",
                "support_launcher_unavailable|无法使用浏览器启动功能。","support_unsafe_url|支持链接因不安全而被拒绝。",
                "updates_heading|SSRename更新","check_startup|启动时检查更新","check_now|立即检查","checking|正在检查更新…","current_version|当前版本","latest_version|最新版本","last_checked|上次检查","release_notes|更新内容","update_available|有新版本可用。","update_none|您正在使用最新版本。","update_button|更新","later_button|稍后","update_source_missing|尚未配置更新源。开发者需要设置GitHub仓库。","update_check_failed|检查更新失败: {0}","update_download|正在下载并验证更新…","update_ready|更新已准备好。关闭ACT后将安装更新，然后请重启ACT。","update_ready_manual|已将验证通过的更新DLL暂存到：\r\n{0}\r\n关闭ACT后，请手动替换SSRename.dll并重新启动ACT。","update_failed|准备更新失败: {0}","update_skipped|将跳过版本 {0}。您可以随时手动重新检查。",
                "watch_paused|文件监视已暂停。","watch_folder_missing|找不到监视文件夹。","destination_required|请选择保存文件夹。","watching|正在监视: {0}","watch_start_failed|无法开始监视: {0}","rename_done|重命名完成: {0}","rename_failed|处理失败: {0} — {1}","write_timeout|无法确认图片写入完成。","context_missing|无法获取角色名或世界名，因此保留了原文件。","watch_error|文件监视错误: {0}","watch_queue_full|截图处理队列已满，原图未移动并保留在原位置。","ffxiv_plugin_missing|FFXIV ACT Plugin未启用。","waiting_character|正在等待角色信息。","started|已启动","stopped|已停止","settings_save_failed|保存设置失败: {0}"
                ,"unknown_character|未知角色","unknown_world|未知世界","unknown_place|未知地点","unknown_time|未知时间","unknown_weather|未知天气","no_weather|无天气","unknown_et|未知ET","collision_limit|同名文件数量已达到上限。","data_missing|缺少内置游戏数据: {0}","ffxiv_read_failed|无法读取FFXIV数据: {0}"
            }); return r;
        }

        private static IDictionary<string, string> KoreanResources()
        {
            var r = CommonEnglish(); Add(r, new[] {
                "subtitle|FFXIV 스크린샷에 촬영 정보를 자동으로 추가합니다","tab_basic|기본 설정","tab_filename|파일 이름","tab_status|상태 및 기록","tab_updates|업데이트","tab_support|후원","language|언어","language_en|영어","language_ja|일본어","language_zh_cn|중국어(간체)","language_ko|한국어",
                "enable_rename|스크린샷 자동 이름 변경 사용","watch_folder|감시할 스크린샷 폴더","browse|찾아보기…","destination|저장 위치","same_destination|감시 폴더에 저장(기본값)","destination_changed|변경한 저장 폴더","folder_actions|폴더 작업","open_destination|현재 저장 위치 열기","basic_note|이미지는 복사되지 않고 이름이 변경되어 이동됩니다. 기존 이미지는 처리하지 않고 새 이미지 만 감시합니다.","template|파일 이름 템플릿","template_note|사용 가능: {character}  {world}  {place}  {timestamp}  {weather}  {et}\r\n{timestamp}는 원본 FFXIV 파일 이름에서 가져오며 확장자는 유지됩니다.","preview|미리 보기","template_actions|템플릿 작업","reset_template|기본 템플릿으로 복원",
                "connection|FFXIV 연결","character|캐릭터","place|장소","weather|날씨","et|에오르제아 시간","file_watch|파일 감시","history|이름 변경 기록","waiting_connection|연결 대기 중","connected|연결됨","initializing|초기화 중","folder_dialog|폴더를 선택하세요","destination_missing|저장 폴더를 찾을 수 없습니다.",
                "support_heading|Roxyz0501의 개발 후원","support_intro|SSRename이 마음에 드셨다면 Ko-fi에서 개발을 자발적으로 후원할 수 있습니다.","support_voluntary|후원은 전적으로 선택 사항입니다. 후원하지 않아도 모든 기능을 사용할 수 있으며 기능 차이는 없습니다.","support_button|Ko-fi에서 Roxyz0501 후원하기","support_click_only|버튼을 명시적으로 클릭한 경우에만 기본 브라우저에서 Ko-fi가 열립니다.","support_opened|기본 브라우저에서 Ko-fi를 열었습니다.","support_failed|Ko-fi를 열 수 없습니다: {0}",
                "support_launcher_unavailable|브라우저 실행 기능을 사용할 수 없습니다.","support_unsafe_url|안전하지 않은 후원 링크가 거부되었습니다.",
                "updates_heading|SSRename 업데이트","check_startup|시작 시 업데이트 확인","check_now|지금 확인","checking|업데이트 확인 중…","current_version|현재 버전","latest_version|최신 버전","last_checked|마지막 확인","release_notes|업데이트 내용","update_available|새 버전이 있습니다.","update_none|최신 버전을 사용 중입니다.","update_button|업데이트","later_button|나중에","update_source_missing|업데이트 원본이 설정되지 않았습니다. 개발자가 GitHub 저장소를 설정해야 합니다.","update_check_failed|업데이트 확인 실패: {0}","update_download|업데이트 다운로드 및 검증 중…","update_ready|업데이트 준비가 완료되었습니다. ACT를 종료하면 설치됩니다. 이후 ACT를 다시 시작하세요.","update_ready_manual|검증된 업데이트 DLL을 다음 위치에 준비했습니다:\r\n{0}\r\nACT를 종료한 뒤 SSRename.dll을 직접 교체하고 ACT를 다시 시작하세요.","update_failed|업데이트 준비 실패: {0}","update_skipped|버전 {0}을 건너뜁니다. 언제든 수동으로 다시 확인할 수 있습니다.",
                "watch_paused|파일 감시가 일시 중지되었습니다.","watch_folder_missing|감시 폴더를 찾을 수 없습니다.","destination_required|저장 폴더를 선택하세요.","watching|감시 중: {0}","watch_start_failed|감시를 시작할 수 없습니다: {0}","rename_done|이름 변경 완료: {0}","rename_failed|처리 실패: {0} — {1}","write_timeout|이미지 쓰기 완료를 확인할 수 없습니다.","context_missing|캐릭터 또는 월드 정보를 가져올 수 없어 원본 이미지를 유지했습니다.","watch_error|파일 감시 오류: {0}","watch_queue_full|스크린샷 처리 대기열이 가득 차 원본 이미지를 이동하지 않았습니다.","ffxiv_plugin_missing|FFXIV ACT Plugin이 활성화되지 않았습니다.","waiting_character|캐릭터 정보를 기다리는 중입니다.","started|시작됨","stopped|중지됨","settings_save_failed|설정 저장 실패: {0}"
                ,"unknown_character|알 수 없는 캐릭터","unknown_world|알 수 없는 월드","unknown_place|알 수 없는 장소","unknown_time|알 수 없는 시간","unknown_weather|알 수 없는 날씨","no_weather|날씨 없음","unknown_et|알 수 없는 ET","collision_limit|같은 이름의 파일 수가 한도에 도달했습니다.","data_missing|내장 게임 데이터가 없습니다: {0}","ffxiv_read_failed|FFXIV 데이터를 읽을 수 없습니다: {0}"
            }); return r;
        }

        private static void Add(IDictionary<string, string> target, IEnumerable<string> pairs)
        {
            foreach (var pair in pairs)
            {
                var separator = pair.IndexOf('|');
                if (separator > 0) target[pair.Substring(0, separator)] = pair.Substring(separator + 1);
            }
        }
    }
}
