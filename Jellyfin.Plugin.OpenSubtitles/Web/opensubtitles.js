const OpenSubtitlesConfig = {
    pluginUniqueId: '4b9ed42f-5185-48b5-9803-6ff2989014c4'
};

function escapeHtml(value) {
    if (value === null || value === undefined) {
        return '';
    }
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function populateLanguageOptions(select) {
    const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/Languages');
    return ApiClient.ajax({ type: 'GET', url })
        .then(function (response) { return response.json(); })
        .then(function (languages) {
            const current = select.value;
            select.innerHTML = '';
            languages.forEach(function (lang) {
                const opt = document.createElement('option');
                opt.value = lang.Code;
                opt.text = lang.Name + ' (' + lang.Code + ')';
                select.appendChild(opt);
            });
            if (current) {
                select.value = current;
            } else {
                select.value = 'en';
            }
        });
}

function renderManualResults(container, results, mediaPath) {
    if (!results.length) {
        container.innerHTML = '<p>No subtitles found.</p>';
        return;
    }

    const rows = results.map(function (r, idx) {
        const flags = [];
        if (r.IsHashMatch) flags.push('hash');
        if (r.HearingImpaired) flags.push('sdh');
        if (r.Forced) flags.push('forced');
        if (r.MachineTranslated) flags.push('mt');
        if (r.AiTranslated) flags.push('ai');

        return '<tr>'
            + '<td>' + escapeHtml(r.Name) + '</td>'
            + '<td>' + escapeHtml(r.Author || '') + '</td>'
            + '<td>' + escapeHtml(r.DownloadCount) + '</td>'
            + '<td>' + escapeHtml((r.CommunityRating || 0).toFixed(1)) + '</td>'
            + '<td>' + escapeHtml(flags.join(', ')) + '</td>'
            + '<td><button is="emby-button" type="button" class="raised" data-row="' + idx + '">Download</button></td>'
            + '</tr>';
    }).join('');

    container.innerHTML = '<table class="detailTable">'
        + '<thead><tr><th>Release</th><th>Uploader</th><th>Downloads</th><th>Rating</th><th>Flags</th><th></th></tr></thead>'
        + '<tbody>' + rows + '</tbody></table>';

    container.querySelectorAll('button[data-row]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            const idx = parseInt(btn.getAttribute('data-row'), 10);
            const r = results[idx];
            if (!mediaPath) {
                Dashboard.processErrorResponse({ statusText: 'Set a media file path before downloading.' });
                return;
            }

            btn.disabled = true;
            const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/Download');
            const data = JSON.stringify({ id: r.Id, destinationPath: mediaPath });
            ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json' })
                .then(function (response) { return response.json(); })
                .then(function (body) {
                    btn.disabled = false;
                    btn.innerText = 'Saved';
                    if (body && body.Path) {
                        btn.title = body.Path;
                    }
                })
                .catch(function (err) {
                    btn.disabled = false;
                    Dashboard.processErrorResponse({ statusText: 'Download failed: ' + (err && err.statusText ? err.statusText : 'unknown error') });
                });
        });
    });
}

export default function (view, params) {
    let credentialsWarning;

    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        credentialsWarning = page.querySelector("#expiredCredentialsWarning");

        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            page.querySelector('#username').value = config.Username || '';
            page.querySelector('#password').value = config.Password || '';
            page.querySelector('#strictMatching').checked = config.StrictMatching !== false;
            if (config.CredentialsInvalid) {
                credentialsWarning.style.display = null;
            }
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });

        const langSelect = page.querySelector('#manualLanguage');
        if (langSelect) {
            populateLanguageOptions(langSelect).catch(function () {
                // Non-fatal: the user can still type a query and re-try.
                const opt = document.createElement('option');
                opt.value = 'en';
                opt.text = 'en';
                langSelect.appendChild(opt);
            });
        }
    });

    view.querySelector('#OpenSubtitlesConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;
        ApiClient.getPluginConfiguration(OpenSubtitlesConfig.pluginUniqueId).then(function (config) {
            const username = form.querySelector('#username').value.trim();
            const password = form.querySelector('#password').value.trim();
            const strictMatching = form.querySelector('#strictMatching').checked;

            if (!username || !password) {
                Dashboard.hideLoadingMsg();
                Dashboard.processErrorResponse({statusText: "Account info is incomplete"});
                return;
            }

            const el = form.querySelector('#ossresponse');
            const saveButton = form.querySelector('#save-button');

            const data = JSON.stringify({ Username: username, Password: password });
            const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo');

            const handler = response => response.json().then(res => {
                saveButton.disabled = false;
                Dashboard.hideLoadingMsg();

                if (response.ok) {
                    el.innerText = `Login info validated, this account can download ${res.Downloads} subtitles per day`;

                    config.Username = username;
                    config.Password = password;
                    config.CredentialsInvalid = false;
                    config.StrictMatching = strictMatching;

                    ApiClient.updatePluginConfiguration(OpenSubtitlesConfig.pluginUniqueId, config).then(function (result) {
                        credentialsWarning.style.display = 'none';
                        Dashboard.processPluginConfigurationUpdateResult(result);
                    }).catch(function () {
                        Dashboard.processErrorResponse({ statusText: "Failed to update plugin configuration" });
                    });
                } else {
                    let msg = res.Message ?? JSON.stringify(res, null, 2);

                    if (msg == 'You cannot consume this service') {
                        msg = 'Invalid API key provided';
                    }

                    Dashboard.processErrorResponse({statusText: `Request failed - ${msg}`});
                }
            }).catch(function () {
                saveButton.disabled = false;
                Dashboard.hideLoadingMsg();
                Dashboard.processErrorResponse({ statusText: "Request failed. Please check your network or server." });
            });

            saveButton.disabled = true;
            ApiClient.ajax({ type: 'POST', url, data, contentType: 'application/json'}).then(handler).catch(handler);

        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: "Failed to load plugin configuration" });
        });
        return false;
    });

    const manualForm = view.querySelector('#OpenSubtitlesManualSearchForm');
    if (manualForm) {
        manualForm.addEventListener('submit', function (e) {
            e.preventDefault();
            const page = view;
            const status = page.querySelector('#manualSearchStatus');
            const resultsEl = page.querySelector('#manualSearchResults');
            const button = page.querySelector('#manualSearchButton');

            const query = page.querySelector('#manualQuery').value.trim();
            const type = page.querySelector('#manualType').value;
            const language = page.querySelector('#manualLanguage').value;
            const season = page.querySelector('#manualSeason').value;
            const episode = page.querySelector('#manualEpisode').value;
            const imdbId = page.querySelector('#manualImdbId').value.trim();
            const year = page.querySelector('#manualYear').value;
            const mediaPath = page.querySelector('#manualMediaPath').value.trim();

            if (!language) {
                status.innerText = 'Pick a language.';
                return false;
            }

            if (!query && !imdbId) {
                status.innerText = 'Enter a title or an IMDb id.';
                return false;
            }

            const payload = {
                query: query || undefined,
                language: language,
                type: type
            };
            if (season) payload.season = parseInt(season, 10);
            if (episode) payload.episode = parseInt(episode, 10);
            if (imdbId) payload.imdbId = parseInt(imdbId.replace(/^tt/, ''), 10);
            if (year) payload.year = parseInt(year, 10);

            button.disabled = true;
            status.innerText = 'Searching...';
            resultsEl.innerHTML = '';

            const url = ApiClient.getUrl('Jellyfin.Plugin.OpenSubtitles/Search');
            ApiClient.ajax({ type: 'POST', url, data: JSON.stringify(payload), contentType: 'application/json' })
                .then(function (response) { return response.json(); })
                .then(function (results) {
                    button.disabled = false;
                    status.innerText = 'Found ' + results.length + ' subtitle(s).';
                    renderManualResults(resultsEl, results, mediaPath);
                })
                .catch(function (err) {
                    button.disabled = false;
                    let msg = 'Search failed';
                    if (err && err.statusText) {
                        msg += ': ' + err.statusText;
                    }
                    status.innerText = msg;
                });

            return false;
        });
    }
}
