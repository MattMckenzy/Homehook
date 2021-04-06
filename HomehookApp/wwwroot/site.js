GetElementWidth = (element, parm) => { return element.getBoundingClientRect().width; };
CenterElement = (element) => {
    var boundingRect = element.getBoundingClientRect();
    element.style.top = (100 - boundingRect.y) + 'px';
    element.style.left = ((document.body.clientWidth / 2) - (boundingRect.width / 2) - boundingRect.x) + 'px';
};
ResetElement = (element) => {
    element.style.top = 0;
    element.style.left = 0;
};