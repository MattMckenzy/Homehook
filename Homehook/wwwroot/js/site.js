GetElementWidth = (element) => { return element.getBoundingClientRect().width; };

ChangeElementHeight = (element, newHeight) => {
    element.style.height = newHeight.toString() + 'px';
};

InitializeTable = (receiverName, dotnetReference, data) => {
    var $table = $('#' + receiverName + 'QueueTable');
    var $playButton = $('#' + receiverName + 'QueuePlayButton');
    var $plusButton = $('#' + receiverName + 'QueuePlusButton');
    var $minusButton = $('#' + receiverName + 'QueueMinusButton');
    var $upButton = $('#' + receiverName + 'QueueUpButton');
    var $downButton = $('#' + receiverName + 'QueueDownButton');

    $table.bootstrapTable({ data: data });

    $table.on('check.bs.table uncheck.bs.table check-all.bs.table uncheck-all.bs.table', function () {
        $playButton.prop('disabled', !$table.bootstrapTable('getSelections').length);
        $minusButton.prop('disabled', !$table.bootstrapTable('getSelections').length);
        $upButton.prop('disabled', !$table.bootstrapTable('getSelections').length);
        $downButton.prop('disabled', !$table.bootstrapTable('getSelections').length);
    });

    $playButton.click(function () {
        var ids = $.map($table.bootstrapTable('getSelections'), function (row) {
            return row.itemId;
        });

        if (Array.isArray(ids) && ids.length)
            dotnetReference.invokeMethodAsync('PlayItem', ids[0]);
    });

    $plusButton.click(function () {
        var ids = $.map($table.bootstrapTable('getSelections'), function (row) {
            return row.itemId;
        });

        var searchTerm = prompt("Please enter Jellyfin search term to find items for " + receiverName + "'s queue. Found items will be inserted before first selected item, or at the end if none selected.", "")

        if (searchTerm != null && searchTerm.length > 0)
            dotnetReference.invokeMethodAsync('AddItems', searchTerm, ids[0]);
    });

    $minusButton.click(function () {
        var ids = $.map($table.bootstrapTable('getSelections'), function (row) {
            return row.itemId;
        });

        if (Array.isArray(ids) && ids.length)
            dotnetReference.invokeMethodAsync('RemoveItems', ids);

        $table.bootstrapTable('uncheckAll');
    });

    $upButton.click(function () {
        var ids = $.map($table.bootstrapTable('getSelections'), function (row) {
            return row.itemId;
        });

        if (Array.isArray(ids) && ids.length)
            dotnetReference.invokeMethodAsync('UpItems', ids);
    });

    $downButton.click(function () {
        var ids = $.map($table.bootstrapTable('getSelections'), function (row) {
            return row.itemId;
        });

        if (Array.isArray(ids) && ids.length)
            dotnetReference.invokeMethodAsync('DownItems', ids);
    });
};

UpdateTable = (tableId, data) => {
    var $table = $('#' + tableId);
    var rows = $table.bootstrapTable('getSelections');
    $table.bootstrapTable('load', data);
    if (Array.isArray(rows) && rows.length)
        data.forEach((item) => {
            if (rows.some((rowItem) => item.itemId == rowItem.itemId))
                $table.bootstrapTable('check', data.indexOf(item));
        });
};

function queueTableHeaderFormatter(row, index) {
    return {
        classes: "text-light"
    };
};


function queueTableRowFormatter(row, index) {
    if (row.isPlaying == true)
        return {
            classes: "bg-success text-light"
        };
    else
        return {
            classes: "text-light"
        };
};

function queueTableDetailFormatter(index, row) {
    var html = []
    $.each(row, function (key, value) {
        if (key == "subtitle")
            html.push('<p class="text-white"><b>' + key + ':</b> ' + value + '</p>');
        else if (key == "runtime")
            html.push('<p class="text-white"><b>' + key + ':</b> ' + new Date(value * 1000).toISOString().substr(11, 8) + '</p>');
    })
    return html.join('');
};
