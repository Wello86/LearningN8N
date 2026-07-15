import '@testing-library/jest-dom/vitest';

// jsdom does not implement scrollIntoView (no layout engine); ChatWidget
// calls it after every message-list update purely as a browser UX nicety.
Element.prototype.scrollIntoView = () => {};
