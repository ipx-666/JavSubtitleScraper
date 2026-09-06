(function () {
  var page = document.querySelector('.JavSubtitleScraperConfigPage');
  var pluginId = '1fbd6b47-0fa2-4f6e-bc3c-8e8c9b3d0f4d';
  if (!page || !window.ApiClient) return;

  page.addEventListener('viewshow', function () {
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
      ['enableScheduledScan', 'enableManualScan', 'enableLibraryEvents', 'forceFullScan', 'overwriteExistingSubtitles'].forEach(function (id) {
        var name = id.charAt(0).toUpperCase() + id.slice(1);
        document.getElementById(id).checked = !!config[name];
      });
      document.getElementById('targetLanguage').value = config.TargetLanguage || 'zh-CN';
      document.getElementById('maxConcurrency').value = config.MaxConcurrency || 1;
    });
  });

  page.addEventListener('submit', function (e) {
    e.preventDefault();
    ApiClient.getPluginConfiguration(pluginId).then(function (config) {
      ['EnableScheduledScan', 'EnableManualScan', 'EnableLibraryEvents', 'ForceFullScan', 'OverwriteExistingSubtitles'].forEach(function (name) {
        config[name] = document.getElementById(name.charAt(0).toLowerCase() + name.slice(1)).checked;
      });
      config.TargetLanguage = document.getElementById('targetLanguage').value || 'zh-CN';
      config.MaxConcurrency = Math.max(1, Math.min(8, parseInt(document.getElementById('maxConcurrency').value || '1', 10)));
      return ApiClient.updatePluginConfiguration(pluginId, config);
    }).then(function () {
      page.querySelector('.statusMessage').textContent = '已保存';
    });
  });
}());
