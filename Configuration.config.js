define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-select', 'emby-checkbox', 'emby-scroller'], function (BaseView, loading) {
    'use strict';

    var pluginId = '1fbd6b47-0fa2-4f6e-bc3c-8e8c9b3d0f4d';

    function updateOptions(view) {
        var manual = view.querySelector('#enableManualScan').checked;
        var scheduled = view.querySelector('#enableScheduledScan').checked;
        var weekly = view.querySelector('#scheduleMode').value === 'Weekly';
        var durationFilter = view.querySelector('#enableDurationFilter').checked;

        view.querySelector('#forceFullScan').disabled = !manual;
        view.querySelector('#durationFilterLowerPercent').disabled = !durationFilter;
        view.querySelector('#durationFilterUpperPercent').disabled = !durationFilter;
        Array.prototype.forEach.call(view.querySelectorAll('#scheduleOptions input, #scheduleOptions select'), function (element) {
            element.disabled = !scheduled;
        });
        view.querySelector('#dailyOptions').style.display = scheduled && !weekly ? '' : 'none';
        view.querySelector('#weeklyOptions').style.display = scheduled && weekly ? '' : 'none';
    }

    function loadPage(view, config) {
        view.querySelector('#enableManualScan').checked = config.EnableManualScan !== false;
        view.querySelector('#forceFullScan').checked = config.ForceFullScan === true;
        view.querySelector('#enableScheduledScan').checked = config.EnableScheduledScan === true;
        view.querySelector('#enableLibraryEvents').checked = config.EnableLibraryEvents !== false;
        view.querySelector('#overwriteExistingSubtitles').checked = config.OverwriteExistingSubtitles === true;
        view.querySelector('#targetLanguage').value = 'zh-CN';
        view.querySelector('#maxConcurrency').value = config.MaxConcurrency || 2;
        view.querySelector('#enableDurationFilter').checked = config.EnableDurationFilter === true;
        view.querySelector('#durationFilterLowerPercent').value = config.DurationFilterLowerPercent || 85;
        view.querySelector('#durationFilterUpperPercent').value = config.DurationFilterUpperPercent || 115;
        view.querySelector('#scheduleMode').value = config.ScheduleMode || 'Daily';
        view.querySelector('#dailyTime').value = config.DailyTime || '03:00:00';
        view.querySelector('#weeklyDay').value = config.WeeklyDay || 'Sunday';
        view.querySelector('#weeklyTime').value = config.WeeklyTime || '03:00:00';
        updateOptions(view);
        loading.hide();
    }

    function onSubmit(event) {
        event.preventDefault();
        loading.show();

        var form = this;
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.EnableManualScan = form.querySelector('#enableManualScan').checked;
            config.ForceFullScan = form.querySelector('#forceFullScan').checked;
            config.EnableScheduledScan = form.querySelector('#enableScheduledScan').checked;
            config.EnableLibraryEvents = form.querySelector('#enableLibraryEvents').checked;
            config.OverwriteExistingSubtitles = form.querySelector('#overwriteExistingSubtitles').checked;
            config.TargetLanguage = 'zh-CN';
            config.MaxConcurrency = Math.max(1, Math.min(8, parseInt(form.querySelector('#maxConcurrency').value || '2', 10)));
            config.EnableDurationFilter = form.querySelector('#enableDurationFilter').checked;
            config.DurationFilterLowerPercent = Math.max(1, Math.min(85, parseInt(form.querySelector('#durationFilterLowerPercent').value || '85', 10)));
            config.DurationFilterUpperPercent = Math.max(115, Math.min(300, parseInt(form.querySelector('#durationFilterUpperPercent').value || '115', 10)));
            config.ScheduleMode = form.querySelector('#scheduleMode').value || 'Daily';
            config.DailyTime = form.querySelector('#dailyTime').value || '03:00:00';
            config.WeeklyDay = form.querySelector('#weeklyDay').value || 'Sunday';
            config.WeeklyTime = form.querySelector('#weeklyTime').value || '03:00:00';

            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(Dashboard.processPluginConfigurationUpdateResult).catch(function () {
            loading.hide();
        });

        return false;
    }

    function View(view, params) {
        BaseView.apply(this, arguments);
        view.querySelector('form').addEventListener('submit', onSubmit);
        ['enableManualScan', 'enableScheduledScan', 'scheduleMode', 'enableDurationFilter'].forEach(function (id) {
            view.querySelector('#' + id).addEventListener('change', function () {
                updateOptions(view);
            });
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function () {
        BaseView.prototype.onResume.apply(this, arguments);
        loading.show();
        var view = this.view;
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            loadPage(view, config);
        }).catch(function () {
            loading.hide();
        });
    };

    return View;
});
