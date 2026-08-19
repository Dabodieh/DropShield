<?php

declare(strict_types=1);

namespace DropShield\Connector\Controller\Adminhtml\Drop;

use DropShield\Connector\Model\ProtectedDropRepository;
use Magento\Backend\App\Action;
use Magento\Framework\Exception\LocalizedException;

class Delete extends Action
{
    public const ADMIN_RESOURCE = 'DropShield_Connector::protected_drops';

    public function __construct(Action\Context $context, private readonly ProtectedDropRepository $repository)
    {
        parent::__construct($context);
    }

    public function execute()
    {
        if (!$this->getRequest()->isPost()) {
            return $this->_redirect('*/*/index');
        }
        try {
            $this->repository->delete((int) $this->getRequest()->getParam('id'));
            $this->messageManager->addSuccessMessage(__('Protected drop deleted.'));
        } catch (LocalizedException $exception) {
            $this->messageManager->addErrorMessage($exception->getMessage());
        }
        return $this->_redirect('*/*/index');
    }
}
