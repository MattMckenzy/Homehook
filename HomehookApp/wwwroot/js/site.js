GetElementWidth = (element) => { return element.getBoundingClientRect().width; };

ChangeElementHeight = (element, newHeight) => {
    element.style.height = newHeight.toString() + 'px';
};

var dotnetReferences = {}

InitializeTable = (tableId, dotnetReference, data) => {
    dotnetReferences[tableId] = dotnetReference;
    $('#' + tableId).bootstrapTable({ data: data });
};

UpdateTable = (tableId, data) => {
    $('#' + tableId).bootstrapTable('load', data);
};

function queueTableButtonsFormatter() {
    return {
        btnPlay: {
            text: 'Play the selected item',
            icon: 'fa-play',
            event: function () {
                if (Array.isArray(this.data) && this.data.filter(item => item.state == true).length)
                    dotnetReferences[this.$el[0].id].invokeMethodAsync('PlayItem',
                        this.data.filter(item => item.state == true).map(item => item.itemId)[0]);
            },
            attributes: {
                title: 'Change the currently playing item to the first selected one in the table.'
            }
        },
        btnUp: {
            text: 'Move items up',
            icon: 'fa-chevron-up',
            event: function () {
                if (Array.isArray(this.data) && this.data.filter(item => item.state == true).length)
                    dotnetReferences[this.$el[0].id].invokeMethodAsync('UpItems',
                        this.data.filter(item => item.state == true).map(item => item.itemId));
            },
            attributes: {
                title: 'Move the selected items up in the queue.'
            }
        },
        btnDown: {
            text: 'Move items down',
            icon: 'fa-chevron-down',
            event: function () {
                if (Array.isArray(this.data) && this.data.filter(item => item.state == true).length)
                    dotnetReferences[this.$el[0].id].invokeMethodAsync('DownItems',
                        this.data.filter(item => item.state == true).map(item => item.itemId));
            },
            attributes: {
                title: 'Move the selected items down in the queue.'
            }
        },
        btnAdd: {
            text: 'Add items',
            icon: 'fa-plus',
            event: function () {
                var searchTerm = prompt("Please enter Jellyfin search term to find items for " + this.$el[0].id.replace("QueueTable", "") + "'s queue. Found items will be inserted before first selected item, or at the end if none selected.", "")
                dotnetReferences[this.$el[0].id].invokeMethodAsync('AddItems', searchTerm,
                    this.data.filter(item => item.state == true).map(item => item.itemId)[0]);
            },
            attributes: {
                title: 'Add items from a Jellyfin search query to the queue.'
            }
        },
        btnRemove: {
            text: 'Remove items',
            icon: 'fa-minus',
            event: function () {
                if (Array.isArray(this.data) && this.data.filter(item => item.state == true).length)
                    dotnetReferences[this.$el[0].id].invokeMethodAsync('RemoveItems',
                        this.data.filter(item => item.state == true).map(item => item.itemId));
            },
            attributes: {
                title: 'Remove the items from the queue.'
            }
        }
    }
};

function queueTableHeaderFormatter(row, index) {
    return {
        classes: "text-light"
    };
};


function queueTableRowFormatter(row, index) {
    if (row.isPlaying == true)
        return {
            classes: "bg-success  text-light"
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
            html.push('<p><b>' + key + ':</b> ' + value + '</p>');
    })
    return html.join('');
};
