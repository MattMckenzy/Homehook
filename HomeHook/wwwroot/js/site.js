GetElementWidth = (element) => { return element.getBoundingClientRect().width; };

ChangeElementHeight = (element, newHeight) => {
    element.style.height = newHeight.toString() + 'px';
};

var currentTooltipElement
var currentTooltip;
ShowTooltip = (tooltipTriggerElement) => {
    currentTooltipElement = tooltipTriggerElement;
    if (tooltipTriggerElement) {
        currentTooltip = bootstrap.Tooltip.getOrCreateInstance(tooltipTriggerElement);
        currentTooltip.show();
    }
};

HideTooltip = () => {
    if (currentTooltip)
        currentTooltip.dispose();
};

UpdateTooltip = (tooltipInnerId, content) => {
    tooltipInnerElement = document.getElementById(tooltipInnerId);
    if (tooltipInnerElement)
        tooltipInnerElement.innerHTML = content;
};