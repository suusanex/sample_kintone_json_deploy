(function () {
  'use strict';

  kintone.events.on('app.record.index.show', function (event) {
    if (document.getElementById('kintone-js-oauth-ci-poc-banner')) {
      return event;
    }

    var element = document.createElement('div');
    element.id = 'kintone-js-oauth-ci-poc-banner';
    element.textContent = 'kintone JS OAuth CI PoC loaded: ' + new Date().toISOString();
    element.style.padding = '8px';
    element.style.margin = '8px 0';
    element.style.border = '1px solid #2f80ed';
    element.style.background = '#eef5ff';

    var space = kintone.app.getHeaderMenuSpaceElement();
    if (space) {
      space.appendChild(element);
    }

    return event;
  });
})();
