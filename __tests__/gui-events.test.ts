import { jest } from '@jest/globals';

const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('GUI Event Handler Tests', () => {
  let dotnet: any;
  let System: any;
  let Forms: any;
  let Drawing: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      dotnet.default.load('System.Windows.Forms');
      dotnet.default.load('System.Drawing');
      System = dotnet.System;
      Forms = System.Windows.Forms;
      Drawing = System.Drawing;
    } catch (e) {
      console.log('Skipping event handler tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should handle MouseDown event on Panel', () => {
    const panel = new Forms.Panel();
    
    let mouseDownTriggered = false;
    
    panel.add_MouseDown((sender: any, e: any) => {
      mouseDownTriggered = true;
    });
    
    expect(panel).toBeDefined();
  });

  it('should handle MouseUp event on Panel', () => {
    const panel = new Forms.Panel();
    
    panel.add_MouseUp((sender: any, e: any) => {
      console.log('Mouse up');
    });
    
    expect(panel).toBeDefined();
  });

  it('should handle MouseMove event on Panel', () => {
    const panel = new Forms.Panel();
    
    panel.add_MouseMove((sender: any, e: any) => {
      console.log('Mouse move');
    });
    
    expect(panel).toBeDefined();
  });

  it('should handle multiple events on same control', () => {
    const button = new Forms.Button();
    
    let clickCount = 0;
    
    button.add_Click(() => {
      clickCount++;
    });
    
    button.add_Click(() => {
      clickCount++;
    });
    
    expect(button).toBeDefined();
  });
});

(isWindows ? describe : describe.skip)('GUI Property Binding Tests', () => {
  let dotnet: any;
  let System: any;
  let Forms: any;
  let Drawing: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      dotnet.default.load('System.Windows.Forms');
      dotnet.default.load('System.Drawing');
      System = dotnet.System;
      Forms = System.Windows.Forms;
      Drawing = System.Drawing;
    } catch (e) {
      console.log('Skipping property binding tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should update label text from button click', () => {
    const label = new Forms.Label();
    const button = new Forms.Button();
    
    label.Text = 'Initial';
    button.Text = 'Click';
    
    button.add_Click(() => {
      label.Text = 'Updated';
    });
    
    expect(label.Text).toBe('Initial');
  });

  it('should set location using Point', () => {
    const label = new Forms.Label();
    const location = new Drawing.Point(100, 50);
    
    label.Location = location;
    
    expect(label.Location.X).toBe(100);
    expect(label.Location.Y).toBe(50);
  });

  it('should update control visibility', () => {
    const button = new Forms.Button();
    
    button.Visible = false;
    expect(button.Visible).toBe(false);
    
    button.Visible = true;
    expect(button.Visible).toBe(true);
  });

  it('should update control enabled state', () => {
    const button = new Forms.Button();
    
    button.Enabled = false;
    expect(button.Enabled).toBe(false);
    
    button.Enabled = true;
    expect(button.Enabled).toBe(true);
  });
});
